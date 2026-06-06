using System;

namespace AimAssistC_
{
    // Fast screen capture logic (to be implemented)
    public class ScreenCapture
    {
        public static System.Drawing.Bitmap FastCapture(System.Drawing.Rectangle detectionBox)
        {
            IntPtr hDesk = NativeMethods.GetDesktopWindow();
            IntPtr hSrce = NativeMethods.GetWindowDC(hDesk);
            IntPtr hDest = NativeMethods.CreateCompatibleDC(hSrce);
            IntPtr hBmp = NativeMethods.CreateCompatibleBitmap(hSrce, detectionBox.Width, detectionBox.Height);
            IntPtr hOldBmp = NativeMethods.SelectObject(hDest, hBmp);
            
            bool b = NativeMethods.BitBlt(hDest, 0, 0, detectionBox.Width, detectionBox.Height, hSrce, detectionBox.Left, detectionBox.Top, NativeMethods.SRCCOPY);
            
            System.Drawing.Bitmap bmp = System.Drawing.Image.FromHbitmap(hBmp);
            
            NativeMethods.SelectObject(hDest, hOldBmp);
            NativeMethods.DeleteObject(hBmp);
            NativeMethods.DeleteDC(hDest);
            NativeMethods.ReleaseDC(hDesk, hSrce);
            
            return bmp;
        }
    }
}
