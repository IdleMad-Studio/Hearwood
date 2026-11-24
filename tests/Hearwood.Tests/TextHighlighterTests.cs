namespace Hearwood.Tests;

public class TextHighlighterTests
{
    const int Lines = 60;

    [Fact]
    public void LightingAWordBelowTheViewScrollsToIt() => OnSta(box =>
    {
        var lit = new TextHighlighter(box, Color.Yellow);
        lit.Show(Start(0), 4);
        Assert.Equal(0, FirstVisibleLine(box));

        int target = Start(40);
        lit.Show(target, 4);

        int top = box.GetPositionFromCharIndex(target).Y;
        Assert.InRange(top, 0, box.ClientSize.Height - box.Font.Height);
        Assert.Equal(39, FirstVisibleLine(box));
    });

    [Fact]
    public void AWordAlreadyInViewDoesNotScroll() => OnSta(box =>
    {
        var lit = new TextHighlighter(box, Color.Yellow);
        lit.Show(Start(0), 4);
        lit.Show(Start(2), 4);
        Assert.Equal(0, FirstVisibleLine(box));
    });

    [Fact]
    public void ScrollingAwayFromTheLitWordStopsTheFollowing() => OnSta(box =>
    {
        var lit = new TextHighlighter(box, Color.Yellow);
        lit.Show(Start(0), 4);
        box.Select(Start(50), 0);
        box.ScrollToCaret();
        int first = FirstVisibleLine(box);
        Assert.True(first > 10);

        lit.Show(Start(1), 4);
        Assert.Equal(first, FirstVisibleLine(box));
    });

    [Fact]
    public void FollowingStartsAgainAfterClear() => OnSta(box =>
    {
        var lit = new TextHighlighter(box, Color.Yellow);
        lit.Show(Start(0), 4);
        box.Select(Start(50), 0);
        box.ScrollToCaret();
        lit.Clear();

        lit.Show(Start(1), 4);
        Assert.Equal(0, FirstVisibleLine(box));
    });

    /// <summary>Where line <paramref name="n"/> starts: each line is "Line NN" and a line break.</summary>
    static int Start(int n) => n * "Line 00\n".Length;

    static int FirstVisibleLine(RichTextBox box) => box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(1, 1)));

    static void OnSta(Action<RichTextBox> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var box = new RichTextBox
                {
                    Width = 300, Height = 200, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Vertical,
                    Font = new Font("Segoe UI", 10f),
                };
                box.CreateControl();
                box.Text = string.Join("\n", Enumerable.Range(0, Lines).Select(i => $"Line {i:00}"));
                box.Select(0, 0);
                test(box);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }
}
