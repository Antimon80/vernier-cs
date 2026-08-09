using Microsoft.Maui.Controls.Shapes;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using GridLength = Microsoft.Maui.GridLength;
using GridUnitType = Microsoft.Maui.GridUnitType;
#endif

namespace App.Views.Controls;

/// <summary>
/// Provides a draggable vertical splitter for resizing tow adjacent columns inside a parent <see cref="Grid"/>.
/// 
/// The splitter resizes the column immediately to its right while enforcing configurable minimum widths
/// for both the left and right content area.
/// 
/// On Windows, native WinUI pointer events are used to support reliable pointer capture while dragging.
/// Other platforms use the .NET MAUI <see cref="PointerGestureRecognizer"/>.  
/// </summary>
public sealed partial class GridSplitter : ContentView
{
    /// <summary>
    /// Width of the right-hand column when the current drag operation started.
    /// </summary>
    private double _initialRightWidth;

    /// <summary>
    /// Horizontal pointer position at the beginning of a Windows drag operation.
    /// </summary>
    private double _startX;

    private Point? _startPointInParent;


    /// <summary>
    /// Indicates whether a splitter drag operation is currently active.
    /// </summary>
    private bool _isDragging;

    /// <summary>
    /// Bindable property backing <see cref="MinimumLeftWidth"/>.
    /// </summary>
    public static readonly BindableProperty MinimumLeftWidthProperty = BindableProperty.Create(
            nameof(MinimumLeftWidth), typeof(double), typeof(GridSplitter), 300.0);

    /// <summary>
    /// Bindable property backing <see cref="MinimumRightWidth"/>.
    /// </summary>
    public static readonly BindableProperty MinimumRightWidthProperty = BindableProperty.Create(
            nameof(MinimumRightWidth), typeof(double), typeof(GridSplitter), 220.0);

    /// <summary>
    /// Gets or sets the minimum width that must remain available to the content area on the left side of the splitter.
    /// </summary>
    public double MinimumLeftWidth
    {
        get => (double)GetValue(MinimumLeftWidthProperty);
        set => SetValue(MinimumLeftWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum width of the column immediately to the right of the splitter.
    /// </summary>
    public double MinimumRightWidth
    {
        get => (double)GetValue(MinimumRightWidthProperty);
        set => SetValue(MinimumRightWidthProperty, value);
    }

    /// <summary>
    /// Initializes the splitter's visual representation and registers the
    /// platform-specific pointer handlers used for dragging.
    /// </summary>
    public GridSplitter()
    {
        BackgroundColor = Colors.Transparent;

        // The visual content must not consume pointer input itself.
        // Pointer events are handeled by the GridSplitter control or its native view.
        Grid visual = new() { InputTransparent = true };

        // Thin vertical line showing the boundary between the two content
        BoxView line = new()
        {
            WidthRequest = 1,
            Color = Colors.LightGray,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Fill
        };

        // Rounded drag handle displayed in the center of the splitter
        Border handle = new()
        {
            WidthRequest = 16,
            HeightRequest = 60,
            StrokeThickness = 1,
            Stroke = Colors.Gray,
            BackgroundColor = Colors.WhiteSmoke,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        visual.Children.Add(line);
        visual.Children.Add(handle);
        Content = visual;

#if WINDOWS
        // Native WinUI pointer handling is used on Windows so the pointer can remain
        // captured when it leaves the splitter during a drag operation
        HandlerChanged += OnHandlerChanged;
#else
        // Other platforms use MAUI's platform-independent pointer recognizer.
        PointerGestureRecognizer pointerGesture = new();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerReleased += OnPointerReleased;
        GestureRecognizers.Add(pointerGesture);
#endif
    }

#if WINDOWS

    /// <summary>
    /// Registers native WinUI pointer events once the MAUI handler and native
    /// platform view are available.
    /// </summary>
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (Handler?.PlatformView is FrameworkElement element)
        {
            element.PointerPressed += OnNativePointerPressed;
            element.PointerMoved += OnNativePointerMoved;
            element.PointerReleased += OnNativePointerReleased;
        }
    }

    /// <summary>
    /// Starts a Windows drag operation and captures the pointer so that movement
    /// continues to be reported outside the splitter's visual bounds.
    /// </summary>
    private void OnNativePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging || Parent is not Grid grid || sender is not UIElement element)
        {
            return;
        }

        int column = Grid.GetColumn(this);

        // The splitter must have one column on either side.
        if (column <= 0 || column >= grid.ColumnDefinitions.Count - 1)
        {
            return;
        }

        element.CapturePointer(e.Pointer);

        _initialRightWidth = GetActualColumnWidth(grid, column + 1);
        _startX = e.GetCurrentPoint(null).Position.X;
        _isDragging = true;
    }

    /// <summary>
    /// Resizes the adjacent right-hand column according to the pointer movement
    /// reported during a Windows drag operation.
    /// </summary>
    private void OnNativePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || Parent is not Grid grid)
        {
            return;
        }

        double currentX = e.GetCurrentPoint(null).Position.X;
        ApplyResize(grid, currentX - _startX);
    }

    /// <summary>
    /// Ends the Windows drag operation and releases the captured pointer.
    /// </summary>
    private void OnNativePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        _isDragging = false;
    }
#else

    /// <summary>
    /// Starts a drag operation on non-Windows platforms and records the initial
    /// pointer position relative to the parent grid.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (_isDragging || Parent is not Grid grid)
        {
            return;
        }

        int column = Grid.GetColumn(this);

        // The splitter must have one column on either side.
        if (column <= 0 || column >= grid.ColumnDefinitions.Count - 1)
        {
            return;
        }

        _initialRightWidth = GetActualColumnWidth(grid, column + 1);
        _startPointInParent = e.GetPosition(grid);
        _isDragging = _startPointInParent is not null;
    }

    /// <summary>
    /// Resizes the adjacent right-hand column according to the current pointer
    /// displacement on non-Windows platforms.
    /// </summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || Parent is not Grid grid || _startPointInParent is not Point start)
        {
            return;
        }

        if (e.GetPosition(grid) is not Point current)
        {
            return;
        }

        ApplyResize(grid, current.X - start.X);
    }

    /// <summary>
    /// Ends the current non-Windows drag operation.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        _isDragging = false;
        _startPointInParent = null;
    }
#endif

    /// <summary>
    /// Applies a horizontal drag displacement to the column immediately to the
    /// right of the splitter.
    ///
    /// The resulting width is constrained so that neither the left nor the
    /// right content area becomes smaller than its configured minimum width.
    /// </summary>
    /// <param name="grid">Parent grid containing the splitter.</param>
    /// <param name="horizontalDelta">
    /// Horizontal pointer displacement since the drag operation started.
    /// Positive values indicate movement to the right.
    /// </param>
    private void ApplyResize(Grid grid, double horizontalDelta)
    {
        int column = Grid.GetColumn(this);
        ColumnDefinition rightColumn = grid.ColumnDefinitions[column + 1];

        // Moving the splitter to the right reduces the width of the right column.
        double newRightWidth = _initialRightWidth - horizontalDelta;

        if (newRightWidth < MinimumRightWidth)
        {
            newRightWidth = MinimumRightWidth;
        }

        // Ensure that resizing the right column does not reduce the left content
        // area below its configured minimum width.
        double leftAvailable = grid.Width - newRightWidth - Width;

        if (leftAvailable < MinimumLeftWidth)
        {
            newRightWidth = grid.Width - MinimumLeftWidth - Width;
        }

        // Convert the resized column to an absolute width because star-sized columns
        // cannot preserve the exact width selected by the user.
        rightColumn.Width = new GridLength(newRightWidth, GridUnitType.Absolute);
        grid.InvalidateMeasure();
    }

    /// <summary>
    /// Determines the currently rendered width of a grid column.
    ///
    /// Absolute column definitions can be read directly. For star-sized or
    /// automatic columns, the method derives the width from the largest child
    /// currently placed in that column.
    /// </summary>
    /// <param name="grid">Grid containing the target column.</param>
    /// <param name="columnIndex">Zero-based index of the target column.</param>
    /// <returns>The current rendered width of the column.</returns>
    private static double GetActualColumnWidth(Grid grid, int columnIndex)
    {
        ColumnDefinition definition = grid.ColumnDefinitions[columnIndex];

        if (definition.Width.IsAbsolute)
        {
            return definition.Width.Value;
        }

        double width = 0;

        foreach (IView child in grid.Children)
        {
            if (Grid.GetColumn((BindableObject)child) == columnIndex)
            {
                width = Math.Max(width, child.Frame.Width);
            }
        }

        return width;
    }
}