// @file UndoHistoryTrimTests.cs
// @author: rudals252
// @brief 실행 취소 기록이 상한을 넘을 때 가장 최근 항목만 원래 순서대로 남는지(순서 역전이 없는지) 검증한다.

using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class UndoHistoryTrimTests
{
    [Fact]
    public void TrimHistory_KeepsNewestEntries_InOriginalOrder()
    {
        var stack = new Stack<int>();
        for (var i = 1; i <= 53; i++)
        {
            stack.Push(i);
            SettingsViewModel.TrimHistory(stack, 50);
        }

        Assert.Equal(50, stack.Count);
        // Undo pops newest first: 53, 52, ..., 4.
        Assert.Equal(Enumerable.Range(4, 50).Reverse(), stack.ToArray());
    }

    [Fact]
    public void TrimHistory_UnderTheLimit_DoesNothing()
    {
        var stack = new Stack<int>(new[] { 1, 2, 3 });

        SettingsViewModel.TrimHistory(stack, 50);

        Assert.Equal(new[] { 3, 2, 1 }, stack.ToArray());
    }
}
