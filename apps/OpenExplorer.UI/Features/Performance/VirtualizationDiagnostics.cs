namespace OpenExplorer_UI.Features.Performance;

public sealed class VirtualizationDiagnostics
{
    public int CurrentRealizedElementCount { get; private set; }

    public int PeakRealizedElementCount { get; private set; }

    public int PreparedElementCount { get; private set; }

    public int ClearedElementCount { get; private set; }

    public void RecordPrepared()
    {
        CurrentRealizedElementCount++;
        PreparedElementCount++;
        PeakRealizedElementCount = Math.Max(PeakRealizedElementCount, CurrentRealizedElementCount);
    }

    public void RecordCleared()
    {
        CurrentRealizedElementCount = Math.Max(0, CurrentRealizedElementCount - 1);
        ClearedElementCount++;
    }
}
