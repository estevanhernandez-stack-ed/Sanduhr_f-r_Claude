using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>5.0 spec 7: the notification shows once, a refused notification is not counted as shown, and nothing
/// here can break a launch.</summary>
public sealed class ArmBuildNoticeServiceTests
{
    private const string Url = "https://example.invalid/releases";

    private sealed class Rig
    {
        public bool Shown;
        public int Toasts;
        public int Marked;
        public bool ToastThrows;
        public bool MarkThrows;
        public List<string> Opened { get; } = [];
        public List<string> Log { get; } = [];

        public ArmBuildNoticeService Make(bool applies) => new(applies, Url,
            alreadyShown: () => Shown,
            markShown: () =>
            {
                if (MarkThrows) throw new IOException("disk");
                Marked++;
                Shown = true;
            },
            showToast: () =>
            {
                if (ToastThrows) throw new InvalidOperationException("no notifications");
                Toasts++;
            },
            openLink: Opened.Add,
            log: Log.Add);
    }

    [Fact]
    public void Due_once_it_shows_marks_and_logs()
    {
        var rig = new Rig();
        var service = rig.Make(applies: true);

        Assert.True(service.ShowIfDue());

        Assert.Equal(1, rig.Toasts);
        Assert.Equal(1, rig.Marked);
        Assert.Contains(ArmBuildNotice.LogDue, rig.Log);
    }

    [Fact]
    public void It_never_shows_twice()
    {
        var rig = new Rig();
        var service = rig.Make(applies: true);
        service.ShowIfDue();

        Assert.False(service.ShowIfDue());

        Assert.Equal(1, rig.Toasts);
        Assert.Equal(1, rig.Marked);
    }

    [Fact]
    public void Where_it_does_not_apply_it_does_nothing()
    {
        var rig = new Rig();

        Assert.False(rig.Make(applies: false).ShowIfDue());

        Assert.Equal(0, rig.Toasts);
        Assert.Equal(0, rig.Marked);
        Assert.Empty(rig.Log);
    }

    [Fact]
    public void A_notification_Windows_refuses_is_not_counted_as_shown()
    {
        var rig = new Rig { ToastThrows = true };
        var service = rig.Make(applies: true);

        Assert.False(service.ShowIfDue());   // and it did not throw

        Assert.Equal(0, rig.Marked);
        Assert.False(rig.Shown);
        // The next launch tries again.
        rig.ToastThrows = false;
        Assert.True(service.ShowIfDue());
        Assert.Equal(1, rig.Toasts);
    }

    [Fact]
    public void A_flag_that_cannot_be_written_does_not_throw()
    {
        var rig = new Rig { MarkThrows = true };

        Assert.True(rig.Make(applies: true).ShowIfDue());

        Assert.Equal(1, rig.Toasts);
    }

    [Fact]
    public void The_button_opens_the_release_list()
    {
        var rig = new Rig();
        var service = rig.Make(applies: true);

        service.OnToast("action=" + ArmBuildNotice.GetAction);

        Assert.Equal([Url], rig.Opened);
    }

    [Theory]
    [InlineData("")]                          // a click on the notification's body
    [InlineData("action=restartToUpdate")]    // another feature's button
    public void Another_click_opens_nothing(string argument)
    {
        var rig = new Rig();

        rig.Make(applies: true).OnToast(argument);

        Assert.Empty(rig.Opened);
    }
}
