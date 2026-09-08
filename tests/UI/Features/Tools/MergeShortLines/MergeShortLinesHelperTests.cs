using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Features.Tools.MergeShortLines;
using System;
using System.Collections.Generic;
using Xunit;

namespace UITests.Features.Tools.MergeShortLines;

public class MergeShortLinesHelperTests
{
    private static SubtitleLineViewModel MakeSubtitle(string text, double startSec, double endSec) =>
        new()
        {
            Text = text,
            StartTime = TimeSpan.FromSeconds(startSec),
            EndTime = TimeSpan.FromSeconds(endSec),
        };

    [Fact]
    public void Merge_RespectsIsMergeAllowedFilter()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle("Line one", 1.0, 2.0),
            MakeSubtitle("Line two", 2.1, 3.0),
            MakeSubtitle("Line three", 3.1, 4.0),
        };

        // All allowed
        var resultAll = MergeShortLinesHelper.Merge(
            subtitles,
            shotChanges: new List<double>(),
            singleLineMaxLength: 50,
            maxNumberOfLines: 2,
            gapThresholdMs: 500,
            unbreakLinesShorterThan: 10);

        Assert.True(resultAll.MergeCount > 0);

        // Disallow merging into line 0
        var resultDisallowed = MergeShortLinesHelper.Merge(
            subtitles,
            shotChanges: new List<double>(),
            singleLineMaxLength: 50,
            maxNumberOfLines: 2,
            gapThresholdMs: 500,
            unbreakLinesShorterThan: 10,
            isMergeAllowed: (target, source) => false);

        Assert.Equal(0, resultDisallowed.MergeCount);
        Assert.Equal(3, resultDisallowed.MergedSubtitles.Count);
    }

    [Fact]
    public void MergeShortLinesItem_ApplyDefaultsToTrue()
    {
        var item = new MergeShortLinesItem("Title", 1, "Fix description", new SubtitleLineViewModel(), 0, 1);
        Assert.True(item.Apply);
        Assert.Equal(0, item.TargetLineIndex);
        Assert.Equal(1, item.SourceLineIndex);

        item.Apply = false;
        Assert.False(item.Apply);
    }
}
