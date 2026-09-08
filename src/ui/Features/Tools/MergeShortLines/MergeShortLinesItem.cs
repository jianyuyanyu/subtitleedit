using CommunityToolkit.Mvvm.ComponentModel;
using Nikse.SubtitleEdit.Features.Main;

namespace Nikse.SubtitleEdit.Features.Tools.MergeShortLines;

public partial class MergeShortLinesItem : ObservableObject
{
    [ObservableProperty] private bool _apply = true;
    public string Name { get; set; }
    public int Number { get; set; }
    public string Fix { get; set; }
    public SubtitleLineViewModel SubtitleLine { get; set; }
    public int TargetLineIndex { get; set; }
    public int SourceLineIndex { get; set; }

    public MergeShortLinesItem(string name, int number, string fix, SubtitleLineViewModel subtitleLine, int targetLineIndex = -1, int sourceLineIndex = -1)
    {
        Name = name;
        Number = number;
        Fix = fix;
        SubtitleLine = subtitleLine;
        TargetLineIndex = targetLineIndex;
        SourceLineIndex = sourceLineIndex;
    }
}
