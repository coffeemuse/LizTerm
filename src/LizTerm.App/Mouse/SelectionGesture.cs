using LizTerm.Core.Screen;

namespace LizTerm.App.Mouse;

/// <summary>What a button release meant: nothing (no press seen), a plain click, or the end of a drag.</summary>
public enum ReleaseResult { None, Click, Drag }

/// <summary>Turns cell-level pointer events into a selection rectangle. Knows nothing about pixels or Avalonia;
/// the screen control hit-tests pointer positions to cells and feeds them here.</summary>
public sealed class SelectionGesture
{
    private (int Row, int Column)? _anchor;
    private bool _dragging;

    /// <summary>The current selection, or null.</summary>
    public ScreenRegion? Region { get; private set; }

    /// <summary>Left button pressed on a cell: becomes the anchor; any existing selection is dropped.</summary>
    public void Press(int row, int column)
    {
        _anchor = (row, column);
        _dragging = false;
        Region = null;
    }

    /// <summary>Pointer moved with the button held. A drag starts once the pointer leaves the anchor cell, so
    /// jitter inside that cell keeps a click a click.</summary>
    public void Move(int row, int column)
    {
        if (_anchor is not { } anchor) return;
        if (!_dragging && anchor == (row, column)) return;
        _dragging = true;
        Region = ScreenRegion.FromCorners(anchor.Row, anchor.Column, row, column);
    }

    /// <summary>Button released. The region is left as it is; the caller moves the cursor on a Click.</summary>
    public ReleaseResult Release()
    {
        if (_anchor is null) return ReleaseResult.None;
        var result = _dragging ? ReleaseResult.Drag : ReleaseResult.Click;
        _anchor = null;
        _dragging = false;
        return result;
    }

    /// <summary>Second press of a double-click: selects the word under the cell, or nothing on a space.
    /// Ends any press sequence so the following release is not a click.</summary>
    public void DoubleClick(int row, int column, ScreenSnapshot snapshot)
    {
        _anchor = null;
        _dragging = false;
        Region = snapshot.WordAt(row, column);
    }
}
