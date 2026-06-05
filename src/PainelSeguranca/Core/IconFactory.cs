using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PainelSeguranca.Core
{
    public enum StatusColor { Green, Yellow, Red, Gray }

    /// <summary>
    /// Desenha icones de status (escudo colorido) em tempo de execucao com GDI+.
    /// Assim nao precisamos de varios arquivos .ico embutidos e o icone da bandeja
    /// muda de cor conforme o status agregado AV+firewall.
    /// </summary>
    public static class IconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Color ToColor(StatusColor c)
        {
            switch (c)
            {
                case StatusColor.Green: return Color.FromArgb(46, 170, 92);
                case StatusColor.Yellow: return Color.FromArgb(225, 170, 30);
                case StatusColor.Red: return Color.FromArgb(210, 60, 55);
                default: return Color.FromArgb(150, 150, 150);
            }
        }

        /// <summary>Cria um Icon de bandeja (16/32) com o escudo na cor do status.</summary>
        public static Icon CreateTrayIcon(StatusColor status)
        {
            using (var bmp = CreateShieldBitmap(32, status))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    // Clona para um Icon gerenciado e libera o handle nativo (evita vazamento de GDI).
                    using (var tmp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        /// <summary>Bitmap do escudo, usado tambem como indicador na tela principal.</summary>
        public static Bitmap CreateShieldBitmap(int size, StatusColor status)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float m = Math.Max(1, size * 0.08f);
                float w = size - 2 * m;
                float h = size - 2 * m;
                float cx = m + w / 2f;
                float top = m;

                using (var path = new GraphicsPath())
                {
                    path.AddLine(cx, top, m + w, top + h * 0.18f);
                    path.AddLine(m + w, top + h * 0.18f, m + w, top + h * 0.55f);
                    path.AddBezier(m + w, top + h * 0.55f, m + w, top + h * 0.85f, cx, top + h, cx, top + h);
                    path.AddBezier(cx, top + h, cx, top + h, m, top + h * 0.85f, m, top + h * 0.55f);
                    path.AddLine(m, top + h * 0.55f, m, top + h * 0.18f);
                    path.CloseFigure();

                    Color baseC = ToColor(status);
                    Color darkC = ControlPaintDarken(baseC, 0.35f);
                    using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(size, size), baseC, darkC))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1, size * 0.05f)))
                        g.DrawPath(pen, path);
                }

                // Simbolo central por status.
                using (var pen = new Pen(Color.White, Math.Max(1.5f, size * 0.075f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (status == StatusColor.Green)
                    {
                        g.DrawLines(pen, new[]
                        {
                            new PointF(m + w * 0.28f, top + h * 0.50f),
                            new PointF(m + w * 0.44f, top + h * 0.66f),
                            new PointF(m + w * 0.74f, top + h * 0.32f)
                        });
                    }
                    else if (status == StatusColor.Red)
                    {
                        g.DrawLine(pen, m + w * 0.34f, top + h * 0.30f, m + w * 0.66f, top + h * 0.62f);
                        g.DrawLine(pen, m + w * 0.66f, top + h * 0.30f, m + w * 0.34f, top + h * 0.62f);
                    }
                    else // Yellow / Gray -> exclamacao
                    {
                        g.DrawLine(pen, cx, top + h * 0.26f, cx, top + h * 0.52f);
                        using (var b = new SolidBrush(Color.White))
                            g.FillEllipse(b, cx - size * 0.045f, top + h * 0.60f, size * 0.09f, size * 0.09f);
                    }
                }
            }
            return bmp;
        }

        private static Color ControlPaintDarken(Color c, float factor)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - factor)),
                (int)(c.G * (1 - factor)),
                (int)(c.B * (1 - factor)));
        }
    }
}
