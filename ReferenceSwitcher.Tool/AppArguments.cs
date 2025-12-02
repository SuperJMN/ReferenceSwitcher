namespace ReferenceSwitcher.Tool;

internal sealed record AppArguments(SwitchMode Mode, string SolutionPath, string ScanDirectory, bool UpdateSolution);
