using System.Collections;

namespace App.Views.Controls;

public partial class MeasurementTableView : ContentView
{
    /// <summary>
    /// Current height of the scroll track, captured from its SizeChanged event once layout has run.
    /// </summary>
    private double _trackHeight;

    /// <summary>
    /// The scroll thumb's top margin at the start of the current drag gesture, used together with
    /// PanUpdatedEventArgs.TotalY (a cumulative delta since the gesture started) to compute its new
    /// position while dragging.
    /// </summary>
    private double _thumbTopAtPanStart;

    /// <summary>
    /// Guards against feeding a scroll position we just set ourselves (from a drag) back through
    /// OnRowsScrolled as if the user had scrolled independently.
    /// </summary>
    private bool _updatingFromDrag;

    /// <summary>
    /// Current width of the horizontal scroll track, captured from its SizeChanged event once layout
    /// has run.
    /// </summary>
    private double _hTrackWidth;

    /// <summary>
    /// The horizontal scroll thumb's left margin at the start of the current drag gesture, used
    /// together with PanUpdatedEventArgs.TotalX (a cumulative delta since the gesture started) to
    /// compute its new position while dragging.
    /// </summary>
    private double _thumbLeftAtPanStart;

    /// <summary>
    /// Guards against feeding a scroll position we just set ourselves (from a horizontal drag) back
    /// through OnTableScrollViewScrolled as if the user had scrolled independently.
    /// </summary>
    private bool _updatingFromHDrag;

    public MeasurementTableView()
    {
        InitializeComponent();
    }

    private void OnScrollTrackSizeChanged(object? sender, EventArgs e)
    {
        _trackHeight = ScrollTrack.Height;
    }

    /// <summary>
    /// Keeps the custom scroll thumb in sync when the row list is scrolled some other way (mouse
    /// wheel, touch, keyboard) instead of by dragging the thumb itself.
    /// </summary>
    private void OnRowsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_updatingFromDrag)
        {
            return;
        }

        int totalCount = GetTotalRowCount();
        double maxTop = GetMaxThumbTop();

        if (totalCount <= 1 || maxTop <= 0)
        {
            return;
        }

        double fraction = Math.Clamp(e.FirstVisibleItemIndex / (double)(totalCount - 1), 0, 1);
        RepositionThumb(fraction * maxTop);
    }

    /// <summary>
    /// Drags the scroll thumb along the track and scrolls the row list to match. Dragging anywhere
    /// on the track (not just the thumb itself) works too, since the thumb has InputTransparent="True"
    /// and the PanGestureRecognizer is attached to the whole track.
    /// </summary>
    private void OnScrollThumbPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _thumbTopAtPanStart = ScrollThumb.Margin.Top;
                break;

            case GestureStatus.Running:
                double maxTop = GetMaxThumbTop();

                if (maxTop <= 0)
                {
                    return;
                }

                double newTop = Math.Clamp(_thumbTopAtPanStart + e.TotalY, 0, maxTop);
                RepositionThumb(newTop);

                int totalCount = GetTotalRowCount();

                if (totalCount > 1)
                {
                    double fraction = newTop / maxTop;
                    int targetIndex = (int)Math.Round(fraction * (totalCount - 1));

                    _updatingFromDrag = true;
                    RowsCollectionView.ScrollTo(targetIndex, position: ScrollToPosition.Start, animate: false);
                    _updatingFromDrag = false;
                }
                break;
        }
    }

    private void RepositionThumb(double top)
    {
        ScrollThumb.Margin = new Thickness(0, top, 0, 0);
    }

    private double GetMaxThumbTop() => Math.Max(0, _trackHeight - ScrollThumb.HeightRequest);

    private int GetTotalRowCount() => (RowsCollectionView.ItemsSource as ICollection)?.Count ?? 0;

    private void OnHScrollTrackSizeChanged(object? sender, EventArgs e)
    {
        _hTrackWidth = HScrollTrack.Width;
    }

    /// <summary>
    /// Keeps the custom horizontal scroll thumb in sync when the table is scrolled some other way
    /// (mouse wheel, touch, keyboard) instead of by dragging the thumb itself.
    /// </summary>
    private void OnTableScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_updatingFromHDrag)
        {
            return;
        }

        double maxScrollX = GetMaxScrollX();
        double maxLeft = GetMaxThumbLeft();

        if (maxScrollX <= 0 || maxLeft <= 0)
        {
            return;
        }

        double fraction = Math.Clamp(e.ScrollX / maxScrollX, 0, 1);
        RepositionHThumb(fraction * maxLeft);
    }

    /// <summary>
    /// Drags the horizontal scroll thumb along the track and scrolls the table to match. Dragging
    /// anywhere on the track (not just the thumb itself) works too, since the thumb has
    /// InputTransparent="True" and the PanGestureRecognizer is attached to the whole track.
    /// </summary>
    private async void OnHScrollThumbPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _thumbLeftAtPanStart = HScrollThumb.Margin.Left;
                break;

            case GestureStatus.Running:
                double maxLeft = GetMaxThumbLeft();

                if (maxLeft <= 0)
                {
                    return;
                }

                double newLeft = Math.Clamp(_thumbLeftAtPanStart + e.TotalX, 0, maxLeft);
                RepositionHThumb(newLeft);

                double maxScrollX = GetMaxScrollX();

                if (maxScrollX > 0)
                {
                    double fraction = newLeft / maxLeft;

                    _updatingFromHDrag = true;
                    await TableScrollView.ScrollToAsync(fraction * maxScrollX, 0, animated: false);
                    _updatingFromHDrag = false;
                }
                break;
        }
    }

    private void RepositionHThumb(double left)
    {
        HScrollThumb.Margin = new Thickness(left, 0, 0, 0);
    }

    private double GetMaxThumbLeft() => Math.Max(0, _hTrackWidth - HScrollThumb.WidthRequest);

    private double GetMaxScrollX() => Math.Max(0, TableContentGrid.Width - TableScrollView.Width);
}
