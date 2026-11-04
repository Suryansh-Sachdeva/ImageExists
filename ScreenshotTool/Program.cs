using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrawingImage = System.Drawing.Image;

namespace ScreenshotTool
{
    internal class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow(); //Identify the desktop window

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd); //Obtain access to the screen's graphics

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc); 

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index); //Determine screen width and height

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc); //Prepare the destination graphics environment

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height); //Create an empty bitmap

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject); //Select the bitmap into the destination DC

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            IntPtr hdcDest,
            int x,
            int y,
            int width,
            int height,
            IntPtr hdcSource,
            int xSource,
            int ySource,
            int rop); //Copy the screen pixels into the bitmap

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SRCCOPY = 0x00CC0020;

        static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: ScreenshotTool.exe <output.png>");

                return 1;
            }

            string outputPath = args[0];

            IntPtr desktopWindow = GetDesktopWindow();
            IntPtr sourceDC = IntPtr.Zero;
            IntPtr memoryDC = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                //Get screen dimensions

                int width = GetSystemMetrics(SM_CXSCREEN);
                int height = GetSystemMetrics(SM_CYSCREEN);

                if (width <= 0 || height <= 0)
                {
                    throw new Exception("Could not determine screen dimensions.");
                }

                //Get the screen graphics access

                sourceDC = GetWindowDC(desktopWindow);

                if (sourceDC == IntPtr.Zero)
                {
                    throw new Exception("Could not access the screen.");
                }

                //Create a compatible memory device context

                memoryDC = CreateCompatibleDC(sourceDC);

                if (memoryDC == IntPtr.Zero)
                {
                    throw new Exception("Could not create memory device context.");
                }

                //Create a bitmap matching the screen dimensions

                bitmap = CreateCompatibleBitmap(sourceDC, width, height);

                if (bitmap == IntPtr.Zero)
                {
                    throw new Exception("Could not create screenshot bitmap.");
                }

                //Store the current bitmap to restore later

                oldBitmap = SelectObject(memoryDC, bitmap);

                if (oldBitmap == IntPtr.Zero)
                {
                    throw new Exception("Could not select screenshot bitmap.");
                }

                //Copy the screen into the bitmap

                bool success = BitBlt(
                    memoryDC,
                    0, 0,
                    width, height,
                    sourceDC,
                    0, 0,
                    SRCCOPY);

                if (!success)
                {
                    throw new Exception("Screen capture failed.");
                }

                //Save the bitmap as a PNG.

                using (Bitmap screenshot =
                    DrawingImage.FromHbitmap(bitmap))
                {
                    screenshot.Save(outputPath, ImageFormat.Png);
                }

                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            finally
            {
                //Delete/Release all Windows resources.

                if (oldBitmap != IntPtr.Zero && memoryDC != IntPtr.Zero)
                {
                    SelectObject(memoryDC, oldBitmap); //Restore old bitmap
                }

                if (bitmap != IntPtr.Zero)
                {
                    DeleteObject(bitmap);
                }

                if (memoryDC != IntPtr.Zero)
                {
                    DeleteDC(memoryDC);
                }

                if (sourceDC != IntPtr.Zero)
                {
                    ReleaseDC(desktopWindow, sourceDC);
                }
            }
        }
    }
}