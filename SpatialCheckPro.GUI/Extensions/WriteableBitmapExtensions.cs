using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpatialCheckPro.GUI.Extensions
{
    /// <summary>
    /// WriteableBitmap 확장 메서드
    /// </summary>
    public static class WriteableBitmapExtensions
    {
        /// <summary>
        /// 지정된 위치에 픽셀 설정
        /// </summary>
        /// <param name="bitmap">대상 비트맵</param>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <param name="color">설정할 색상</param>
        public static void SetPixel(this WriteableBitmap bitmap, int x, int y, Color color)
        {
            if (bitmap == null) return;
            if (x < 0 || x >= bitmap.PixelWidth || y < 0 || y >= bitmap.PixelHeight) return;

            try
            {
                bitmap.Lock();

                unsafe
                {
                    // 픽셀 포인터 계산
                    IntPtr pBackBuffer = bitmap.BackBuffer;
                    pBackBuffer += y * bitmap.BackBufferStride;
                    pBackBuffer += x * 4; // 4 bytes per pixel (BGRA)

                    // BGRA 형식으로 색상 설정
                    int colorData = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
                    *((int*)pBackBuffer) = colorData;
                }

                // 변경된 영역 표시
                bitmap.AddDirtyRect(new System.Windows.Int32Rect(x, y, 1, 1));
            }
            finally
            {
                bitmap.Unlock();
            }
        }

        /// <summary>
        /// 지정된 위치의 픽셀 색상 가져오기
        /// </summary>
        /// <param name="bitmap">대상 비트맵</param>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>픽셀 색상</returns>
        public static Color GetPixel(this WriteableBitmap bitmap, int x, int y)
        {
            if (bitmap == null) return Colors.Transparent;
            if (x < 0 || x >= bitmap.PixelWidth || y < 0 || y >= bitmap.PixelHeight) return Colors.Transparent;

            try
            {
                bitmap.Lock();

                unsafe
                {
                    // 픽셀 포인터 계산
                    IntPtr pBackBuffer = bitmap.BackBuffer;
                    pBackBuffer += y * bitmap.BackBufferStride;
                    pBackBuffer += x * 4; // 4 bytes per pixel (BGRA)

                    // BGRA 형식에서 색상 추출
                    int colorData = *((int*)pBackBuffer);
                    byte a = (byte)((colorData >> 24) & 0xFF);
                    byte r = (byte)((colorData >> 16) & 0xFF);
                    byte g = (byte)((colorData >> 8) & 0xFF);
                    byte b = (byte)(colorData & 0xFF);

                    return Color.FromArgb(a, r, g, b);
                }
            }
            finally
            {
                bitmap.Unlock();
            }
        }

        /// <summary>
        /// 원 그리기
        /// </summary>
        /// <param name="bitmap">대상 비트맵</param>
        /// <param name="centerX">중심 X 좌표</param>
        /// <param name="centerY">중심 Y 좌표</param>
        /// <param name="radius">반지름</param>
        /// <param name="color">색상</param>
        public static void DrawCircle(this WriteableBitmap bitmap, int centerX, int centerY, int radius, Color color)
        {
            if (bitmap == null || radius <= 0) return;

            try
            {
                bitmap.Lock();

                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    for (int y = centerY - radius; y <= centerY + radius; y++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            if (x >= 0 && x < bitmap.PixelWidth && y >= 0 && y < bitmap.PixelHeight)
                            {
                                unsafe
                                {
                                    IntPtr pBackBuffer = bitmap.BackBuffer;
                                    pBackBuffer += y * bitmap.BackBufferStride;
                                    pBackBuffer += x * 4;

                                    int colorData = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
                                    *((int*)pBackBuffer) = colorData;
                                }
                            }
                        }
                    }
                }

                // 변경된 영역 표시
                var dirtyRect = new System.Windows.Int32Rect(
                    Math.Max(0, centerX - radius),
                    Math.Max(0, centerY - radius),
                    Math.Min(bitmap.PixelWidth, (centerX + radius) * 2) - Math.Max(0, centerX - radius),
                    Math.Min(bitmap.PixelHeight, (centerY + radius) * 2) - Math.Max(0, centerY - radius)
                );
                
                if (dirtyRect.Width > 0 && dirtyRect.Height > 0)
                {
                    bitmap.AddDirtyRect(dirtyRect);
                }
            }
            finally
            {
                bitmap.Unlock();
            }
        }

        /// <summary>
        /// 사각형 그리기
        /// </summary>
        /// <param name="bitmap">대상 비트맵</param>
        /// <param name="centerX">중심 X 좌표</param>
        /// <param name="centerY">중심 Y 좌표</param>
        /// <param name="size">크기</param>
        /// <param name="color">색상</param>
        public static void DrawSquare(this WriteableBitmap bitmap, int centerX, int centerY, int size, Color color)
        {
            if (bitmap == null || size <= 0) return;

            int halfSize = size / 2;
            int left = centerX - halfSize;
            int top = centerY - halfSize;
            int right = centerX + halfSize;
            int bottom = centerY + halfSize;

            try
            {
                bitmap.Lock();

                for (int x = left; x <= right; x++)
                {
                    for (int y = top; y <= bottom; y++)
                    {
                        if (x >= 0 && x < bitmap.PixelWidth && y >= 0 && y < bitmap.PixelHeight)
                        {
                            unsafe
                            {
                                IntPtr pBackBuffer = bitmap.BackBuffer;
                                pBackBuffer += y * bitmap.BackBufferStride;
                                pBackBuffer += x * 4;

                                int colorData = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
                                *((int*)pBackBuffer) = colorData;
                            }
                        }
                    }
                }

                // 변경된 영역 표시
                var dirtyRect = new System.Windows.Int32Rect(
                    Math.Max(0, left),
                    Math.Max(0, top),
                    Math.Min(bitmap.PixelWidth, right) - Math.Max(0, left),
                    Math.Min(bitmap.PixelHeight, bottom) - Math.Max(0, top)
                );
                
                if (dirtyRect.Width > 0 && dirtyRect.Height > 0)
                {
                    bitmap.AddDirtyRect(dirtyRect);
                }
            }
            finally
            {
                bitmap.Unlock();
            }
        }

        /// <summary>
        /// 삼각형 그리기
        /// </summary>
        /// <param name="bitmap">대상 비트맵</param>
        /// <param name="centerX">중심 X 좌표</param>
        /// <param name="centerY">중심 Y 좌표</param>
        /// <param name="size">크기</param>
        /// <param name="color">색상</param>
        public static void DrawTriangle(this WriteableBitmap bitmap, int centerX, int centerY, int size, Color color)
        {
            if (bitmap == null || size <= 0) return;

            int halfSize = size / 2;
            
            try
            {
                bitmap.Lock();

                // 간단한 삼각형 그리기 (정삼각형)
                for (int y = centerY - halfSize; y <= centerY + halfSize; y++)
                {
                    int width = (int)((halfSize * 2) * (1.0 - Math.Abs(y - centerY) / (double)halfSize));
                    int left = centerX - width / 2;
                    int right = centerX + width / 2;

                    for (int x = left; x <= right; x++)
                    {
                        if (x >= 0 && x < bitmap.PixelWidth && y >= 0 && y < bitmap.PixelHeight)
                        {
                            unsafe
                            {
                                IntPtr pBackBuffer = bitmap.BackBuffer;
                                pBackBuffer += y * bitmap.BackBufferStride;
                                pBackBuffer += x * 4;

                                int colorData = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
                                *((int*)pBackBuffer) = colorData;
                            }
                        }
                    }
                }

                // 변경된 영역 표시
                var dirtyRect = new System.Windows.Int32Rect(
                    Math.Max(0, centerX - halfSize),
                    Math.Max(0, centerY - halfSize),
                    Math.Min(bitmap.PixelWidth, centerX + halfSize) - Math.Max(0, centerX - halfSize),
                    Math.Min(bitmap.PixelHeight, centerY + halfSize) - Math.Max(0, centerY - halfSize)
                );
                
                if (dirtyRect.Width > 0 && dirtyRect.Height > 0)
                {
                    bitmap.AddDirtyRect(dirtyRect);
                }
            }
            finally
            {
                bitmap.Unlock();
            }
        }

        /// <summary>
        /// 십자가 그리기
        /// </summary>
        /// <param name="bitmap">대상 비트맵</param>
        /// <param name="centerX">중심 X 좌표</param>
        /// <param name="centerY">중심 Y 좌표</param>
        /// <param name="size">크기</param>
        /// <param name="color">색상</param>
        public static void DrawCross(this WriteableBitmap bitmap, int centerX, int centerY, int size, Color color)
        {
            if (bitmap == null || size <= 0) return;

            int halfSize = size / 2;
            int thickness = Math.Max(1, size / 6);

            try
            {
                bitmap.Lock();

                // 수직선
                for (int x = centerX - thickness / 2; x <= centerX + thickness / 2; x++)
                {
                    for (int y = centerY - halfSize; y <= centerY + halfSize; y++)
                    {
                        if (x >= 0 && x < bitmap.PixelWidth && y >= 0 && y < bitmap.PixelHeight)
                        {
                            unsafe
                            {
                                IntPtr pBackBuffer = bitmap.BackBuffer;
                                pBackBuffer += y * bitmap.BackBufferStride;
                                pBackBuffer += x * 4;

                                int colorData = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
                                *((int*)pBackBuffer) = colorData;
                            }
                        }
                    }
                }

                // 수평선
                for (int y = centerY - thickness / 2; y <= centerY + thickness / 2; y++)
                {
                    for (int x = centerX - halfSize; x <= centerX + halfSize; x++)
                    {
                        if (x >= 0 && x < bitmap.PixelWidth && y >= 0 && y < bitmap.PixelHeight)
                        {
                            unsafe
                            {
                                IntPtr pBackBuffer = bitmap.BackBuffer;
                                pBackBuffer += y * bitmap.BackBufferStride;
                                pBackBuffer += x * 4;

                                int colorData = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
                                *((int*)pBackBuffer) = colorData;
                            }
                        }
                    }
                }

                // 변경된 영역 표시
                var dirtyRect = new System.Windows.Int32Rect(
                    Math.Max(0, centerX - halfSize),
                    Math.Max(0, centerY - halfSize),
                    Math.Min(bitmap.PixelWidth, centerX + halfSize) - Math.Max(0, centerX - halfSize),
                    Math.Min(bitmap.PixelHeight, centerY + halfSize) - Math.Max(0, centerY - halfSize)
                );
                
                if (dirtyRect.Width > 0 && dirtyRect.Height > 0)
                {
                    bitmap.AddDirtyRect(dirtyRect);
                }
            }
            finally
            {
                bitmap.Unlock();
            }
        }
    }
}