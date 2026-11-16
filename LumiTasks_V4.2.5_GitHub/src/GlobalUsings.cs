// Keep shared .NET namespaces explicit. WPF and WinForms expose multiple
// identically named types (Button, Brush, Image, Point, etc.).
// Do not globally import System.Drawing or System.Windows.Forms.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
