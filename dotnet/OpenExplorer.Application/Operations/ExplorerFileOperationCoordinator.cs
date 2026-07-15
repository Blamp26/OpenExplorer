using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Operations;

/// <summary>
/// Coordinates a single mutation batch against the accepted navigation state.
/// Providers own all platform-specific mutation work; this type owns generation
/// checks, busy state, and the single refresh following an accepted mutation.
/// </summary>
public sealed class ExplorerFileOperationCoordinator : IDisposable
{
    private readonly ExplorerNavigationController navigation;
    private readonly IExplorerFileOperationProvider provider;
    private int active;
    private bool disposed;

    public ExplorerFileOperationCoordinator(
        ExplorerNavigationController navigation,
        IExplorerFileOperationProvider provider)
    {
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public event EventHandler? StateChanged;

    public bool IsBusy => Volatile.Read(ref active) != 0;
    public string? ErrorMessage { get; private set; }

    public async Task<ExplorerFileOperationResult> ExecuteAsync(
        ExplorerFileOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        if (Interlocked.Exchange(ref active, 1) != 0)
        {
            return Failure("Another file operation is already in progress.");
        }

        ErrorMessage = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            ExplorerLocation acceptedLocation = navigation.CurrentLocation
                ?? throw new InvalidOperationException("There is no accepted location.");
            long acceptedGeneration = navigation.Generation;
            if (navigation.IsBusy || request.Generation != acceptedGeneration || !navigation.IsCurrentLocation(request.Location))
            {
                return Failure("The operation target is no longer current.");
            }

            ExplorerFileOperationResult result;
            try
            {
                result = await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ExplorerFileOperationResult(ExplorerFileOperationStatus.Cancelled, Array.Empty<ExplorerFileOperationFailure>(), false);
            }

            if (!IsCurrent(acceptedLocation, acceptedGeneration))
            {
                // The provider result is still consumed and returned, but it cannot
                // alter the now-current directory or surface a stale UI error.
                return result with { Mutated = false, CreatedItemId = null };
            }

            if (result.Mutated)
            {
                // One batch has one refresh, even when the provider reports many
                // successful or failed items.
                Task refreshTask = navigation.RefreshAsync();
                long refreshGeneration = navigation.Generation;
                await refreshTask.ConfigureAwait(true);
                if (result.CreatedItemId is ulong createdId
                    && navigation.Generation == refreshGeneration
                    && navigation.IsCurrentLocation(acceptedLocation)
                    && navigation.CurrentItems is { } items)
                {
                    navigation.Selection.TrySelectItem(items, createdId);
                }
            }

            if (result.Failures.Count > 0 && navigation.IsCurrentLocation(acceptedLocation))
            {
                ErrorMessage = result.Failures.Count == 1
                    ? result.Failures[0].Message
                    : $"{result.Failures.Count} item operations failed.";
                StateChanged?.Invoke(this, EventArgs.Empty);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!disposed) ErrorMessage = exception.Message;
            return Failure(exception.Message);
        }
        finally
        {
            Volatile.Write(ref active, 0);
            if (!disposed) StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        disposed = true;
        ErrorMessage = null;
        Volatile.Write(ref active, 0);
        StateChanged = null;
        GC.SuppressFinalize(this);
    }

    private bool IsCurrent(ExplorerLocation location, long generation) =>
        !disposed && navigation.Generation == generation && navigation.IsCurrentLocation(location);

    private static ExplorerFileOperationResult Failure(string message) =>
        new(ExplorerFileOperationStatus.Failed, [new ExplorerFileOperationFailure(null, null, message)], false);
}
