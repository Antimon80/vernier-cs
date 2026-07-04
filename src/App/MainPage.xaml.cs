namespace App {
    public partial class MainPage : ContentPage {
        int count = 0;

        public MainPage() {
            InitializeComponent();
        }

        private void OnCounterClicked(object? sender, EventArgs e) {
            count++;

            if (count == 1)
#pragma warning disable CA1416 // Validate platform compatibility
                CounterBtn.Text = $"Clicked {count} time";
#pragma warning restore CA1416 // Validate platform compatibility
            else
#pragma warning disable CA1416 // Validate platform compatibility
                CounterBtn.Text = $"Clicked {count} times";
#pragma warning restore CA1416 // Validate platform compatibility

#pragma warning disable CA1416 // Validate platform compatibility
            SemanticScreenReader.Announce(CounterBtn.Text);
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }
}
