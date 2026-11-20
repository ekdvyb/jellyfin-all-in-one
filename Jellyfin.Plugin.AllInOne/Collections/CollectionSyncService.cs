using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AllInOne.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AllInOne.Collections;

/// <summary>
/// Keeps a set of automatically managed collections in sync with the library.
/// </summary>
public sealed class CollectionSyncService : IDisposable
{
    /// <summary>
    /// Provider id key used to mark collections owned by this plugin.
    /// </summary>
    public const string ManagedProviderKey = "JellyfinAllInOne";

    private const string ManagedProviderValue = "managed";

    private readonly ICollectionManager _collectionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CollectionSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Timer _debounceTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionSyncService"/> class.
    /// </summary>
    public CollectionSyncService(
        ICollectionManager collectionManager,
        ILibraryManager libraryManager,
        ILogger<CollectionSyncService> logger)
    {
        _collectionManager = collectionManager;
        _libraryManager = libraryManager;
        _logger = logger;

        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _debounceTimer = new Timer(OnDebounce, null, Timeout.Infinite, Timeout.Infinite);
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs args)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableAutoCollections != true || !config.RunOnLibraryChange)
        {
            return;
        }

        if (args.Item is Movie or Series or Episode or Video)
        {
            // Library scans fire many events; debounce so we only sync once at the end.
            _debounceTimer.Change(TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounce(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SyncAsync(null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while synchronizing automatic collections.");
            }
        });
    }

    /// <summary>
    /// Rebuilds all managed collections.
    /// </summary>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.EnableAutoCollections)
        {
            return;
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Starting automatic collection sync.");
            progress?.Report(5);

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes =
                [
                    BaseItemKind.Movie,
                    BaseItemKind.Series,
                    BaseItemKind.Episode,
                    BaseItemKind.Video,
                    BaseItemKind.MusicVideo
                ],
                Recursive = true,
                IsVirtualItem = false,
                CollapseBoxSetItems = false
            });

            progress?.Report(20);
            var desired = SmartGroupBuilder.Build(items, config);
            _logger.LogInformation("Desired automatic collections: {Count}", desired.Count);
            progress?.Report(35);

            var managed = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.BoxSet],
                Recursive = true
            })
            .OfType<BoxSet>()
            .Where(c => c.ProviderIds.TryGetValue(ManagedProviderKey, out var value)
                        && string.Equals(value, ManagedProviderValue, StringComparison.OrdinalIgnoreCase))
            .ToList();

            // Remove managed collections that are no longer wanted (e.g. a setting was turned off).
            foreach (var stale in managed.Where(c => !desired.ContainsKey(c.Name ?? string.Empty)))
            {
                try
                {
                    _libraryManager.DeleteItem(stale, new DeleteOptions());
                    _logger.LogInformation("Removed obsolete collection '{Name}'.", stale.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove obsolete collection '{Name}'.", stale.Name);
                }
            }

            var step = 60d;
            var stepSize = desired.Count == 0 ? 40d : 40d / desired.Count;
            foreach (var (name, itemIds) in desired)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var collection = managed.FirstOrDefault(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

                if (collection is null)
                {
                    collection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                    {
                        Name = name,
                        IsLocked = true,
                        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [ManagedProviderKey] = ManagedProviderValue
                        }
                    }).ConfigureAwait(false);

                    managed.Add(collection);
                    _logger.LogInformation("Created collection '{Name}'.", name);
                }

                await EnsureManagedMetadataAsync(collection, cancellationToken).ConfigureAwait(false);

                var currentIds = collection.GetLinkedChildren()
                    .Select(i => i.Id)
                    .ToHashSet();

                var toRemove = currentIds.Where(id => !itemIds.Contains(id)).ToList();
                var toAdd = itemIds.Where(id => !currentIds.Contains(id)).ToList();

                if (toRemove.Count > 0)
                {
                    await _collectionManager.RemoveFromCollectionAsync(collection.Id, toRemove)
                        .ConfigureAwait(false);
                }

                if (toAdd.Count > 0)
                {
                    await _collectionManager.AddToCollectionAsync(collection.Id, toAdd)
                        .ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Collection '{Name}': {Total} items ({Added} added, {Removed} removed).",
                    name,
                    itemIds.Count,
                    toAdd.Count,
                    toRemove.Count);

                step += stepSize;
                progress?.Report(step);
            }

            progress?.Report(100);
            _logger.LogInformation("Automatic collection sync finished.");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task EnsureManagedMetadataAsync(BoxSet collection, CancellationToken cancellationToken)
    {
        var changed = false;

        if (!collection.IsLocked)
        {
            collection.IsLocked = true;
            changed = true;
        }

        var locked = collection.LockedFields?.ToList() ?? [];
        var wanted = new[] { MetadataField.Name, MetadataField.Overview };
        foreach (var field in wanted.Where(f => !locked.Contains(f)))
        {
            locked.Add(field);
            changed = true;
        }

        if (changed)
        {
            collection.LockedFields = locked.ToArray();
            await _libraryManager.UpdateItemAsync(
                collection,
                collection.GetParent(),
                ItemUpdateType.MetadataEdit,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _debounceTimer.Dispose();
        _syncLock.Dispose();
    }
}
