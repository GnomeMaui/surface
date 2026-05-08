using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using GnomeSurfaceCanvas.Services;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceCanvas;

public readonly record struct MonitorLayout(int X, int Y, int Width, int Height);
