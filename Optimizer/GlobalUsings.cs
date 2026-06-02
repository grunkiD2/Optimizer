// WinForms interop (UseWindowsForms) pulls System.Windows.Forms and System.Drawing into the
// global usings, which collide with WPF types. These aliases keep the unqualified names pointing
// at the WPF types the app actually uses.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Point = System.Windows.Point;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using DragEventArgs = System.Windows.DragEventArgs;
global using DataFormats = System.Windows.DataFormats;
