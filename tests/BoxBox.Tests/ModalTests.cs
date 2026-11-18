using BoxBox.Web.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BoxBox.Tests;

public class ModalTests : BunitContext
{
    public ModalTests() => JSInterop.Mode = JSRuntimeMode.Loose; // focusing the dialog calls into the browser

    private IRenderedComponent<Modal> Open(Action? onClose = null, bool isOpen = true) =>
        Render<Modal>(p => p
            .Add(x => x.IsOpen, isOpen)
            .Add(x => x.Title, "Add fill-up")
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke()))
            .AddChildContent("<p id=\"inside\">Hello</p>"));

    [Fact]
    public void Closed_modal_renders_nothing()
    {
        var cut = Open(isOpen: false);
        Assert.Equal("", cut.Markup.Trim());
    }

    [Fact]
    public void Open_modal_is_an_accessible_dialog_with_its_title_and_content()
    {
        var cut = Open();
        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("modal-title", dialog.GetAttribute("aria-labelledby"));
        Assert.Equal("Add fill-up", cut.Find("#modal-title").TextContent);
        Assert.NotNull(cut.Find("#inside"));
    }

    [Fact]
    public void The_close_button_asks_the_parent_to_close()
    {
        var closed = 0;
        var cut = Open(() => closed++);
        cut.Find("button[aria-label=Close]").Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Escape_closes_but_other_keys_do_not()
    {
        var closed = 0;
        var cut = Open(() => closed++);
        cut.Find(".backdrop").KeyDown(new KeyboardEventArgs { Key = "a" });
        Assert.Equal(0, closed);
        cut.Find(".backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Tapping_the_backdrop_closes_but_tapping_inside_the_panel_does_not()
    {
        var closed = 0;
        var cut = Open(() => closed++);
        // A click inside the panel stops at the panel (@onclick:stopPropagation) and never reaches the backdrop's
        // handler. bUnit reports "no handler" for a click that nothing receives, so that exception is the proof.
        Assert.Throws<MissingEventHandlerException>(() => cut.Find("#inside").Click());
        Assert.Throws<MissingEventHandlerException>(() => cut.Find(".modal").Click());
        Assert.Equal(0, closed);
        cut.Find(".backdrop").Click();         // the dark area outside the panel
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Keyboard_focus_moves_into_the_dialog_when_it_opens()
    {
        var cut = Open();
        JSInterop.VerifyFocusAsyncInvoke();
    }
}
