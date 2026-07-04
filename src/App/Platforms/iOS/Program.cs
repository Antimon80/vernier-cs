using ObjCRuntime;
using UIKit;

namespace App {
    public class Program {
        // This is the main entry point of the application.
        static void Main(string[] args) {
            // if you want to use a different Application Delegate class from "AppDelegate"
            // you can specify it here.
#pragma warning disable CA1416 // Validate platform compatibility
            UIApplication.Main(args, null, typeof(AppDelegate));
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }
}
