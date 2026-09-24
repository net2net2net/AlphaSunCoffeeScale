// android TFM 的 ImplicitUsings 引入 Android.App/Android.Widget/Android.OS 等命名空间，
// 与 Avalonia 的同名类型歧义（CS0104）。用 global using 别名明确指向 Avalonia 类型。
#if ANDROID
global using Application = Avalonia.Application;
global using Button = Avalonia.Controls.Button;
global using DatePicker = Avalonia.Controls.DatePicker;
global using Orientation = Avalonia.Layout.Orientation;
global using ProgressBar = Avalonia.Controls.ProgressBar;
#endif
