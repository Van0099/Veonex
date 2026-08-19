using System;
using System.Runtime.CompilerServices;
using Veonex.Mathematics;

namespace Veonex.Mathematics
{
    public static class PerlinNoise
    {
        // Таблица перестановок для шума Перлина
        private static readonly int[] Permutation = new int[512];
        private static readonly int[] P = new int[512];
        private static readonly DVector3[] Gradients = new DVector3[256];
        private static readonly Random Random = new();

        static PerlinNoise()
        {
            Initialize(42); // Стандартный seed
        }

        /// <summary>
        /// Инициализирует генератор шума с указанным seed
        /// </summary>
        public static void Initialize(int seed)
        {
            var rand = new Random(seed);

            // Заполнение таблицы градиентов
            for (int i = 0; i < 256; i++)
            {
                Gradients[i] = GenerateRandomGradient(rand);
            }

            // Заполнение таблицы перестановок
            var perm = new int[256];
            for (int i = 0; i < 256; i++)
                perm[i] = i;

            // Перемешивание
            for (int i = 255; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }

            // Дублирование для облегчения доступа
            for (int i = 0; i < 512; i++)
            {
                Permutation[i] = perm[i & 255];
                P[i] = perm[i & 255];
            }
        }

        private static DVector3 GenerateRandomGradient(Random rand)
        {
            // Генерация случайного единичного вектора на сфере
            double theta = rand.NextDouble() * DMath.TwoPi;
            double phi = Math.Acos(2.0 * rand.NextDouble() - 1.0);

            return new DVector3(
                Math.Sin(phi) * Math.Cos(theta),
                Math.Sin(phi) * Math.Sin(theta),
                Math.Cos(phi)
            );
        }

        /// <summary>
        /// Генерирует 2D шум Перлина в точке (x, y)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Noise2D(double x, double y)
        {
            // Нахождение ячейки
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;

            // Относительные координаты внутри ячейки [0, 1]
            double xf = x - Math.Floor(x);
            double yf = y - Math.Floor(y);

            // Сглаживание
            double u = Fade(xf);
            double v = Fade(yf);

            // Индексы для таблицы перестановок
            int aaa = P[P[xi] + yi];
            int aba = P[P[xi] + yi + 1];
            int aab = P[P[xi + 1] + yi];
            int abb = P[P[xi + 1] + yi + 1];

            // Вычисление градиентов и интерполяция
            double x1 = DMath.Lerp(
                Grad(aaa, xf, yf, 0.0),
                Grad(aab, xf - 1.0, yf, 0.0),
                u
            );

            double x2 = DMath.Lerp(
                Grad(aba, xf, yf - 1.0, 0.0),
                Grad(abb, xf - 1.0, yf - 1.0, 0.0),
                u
            );

            return DMath.Lerp(x1, x2, v);
        }

        /// <summary>
        /// Генерирует 3D шум Перлина в точке (x, y, z)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Noise3D(double x, double y, double z)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            int zi = (int)Math.Floor(z) & 255;

            double xf = x - Math.Floor(x);
            double yf = y - Math.Floor(y);
            double zf = z - Math.Floor(z);

            double u = Fade(xf);
            double v = Fade(yf);
            double w = Fade(zf);

            int aaa = P[P[P[xi] + yi] + zi];
            int aba = P[P[P[xi] + yi + 1] + zi];
            int aab = P[P[P[xi + 1] + yi] + zi];
            int abb = P[P[P[xi + 1] + yi + 1] + zi];
            int baa = P[P[P[xi] + yi] + zi + 1];
            int bba = P[P[P[xi] + yi + 1] + zi + 1];
            int bab = P[P[P[xi + 1] + yi] + zi + 1];
            int bbb = P[P[P[xi + 1] + yi + 1] + zi + 1];

            double x1 = DMath.Lerp(
                Grad(aaa, xf, yf, zf),
                Grad(aab, xf - 1.0, yf, zf),
                u
            );

            double x2 = DMath.Lerp(
                Grad(aba, xf, yf - 1.0, zf),
                Grad(abb, xf - 1.0, yf - 1.0, zf),
                u
            );

            double x3 = DMath.Lerp(
                Grad(baa, xf, yf, zf - 1.0),
                Grad(bab, xf - 1.0, yf, zf - 1.0),
                u
            );

            double x4 = DMath.Lerp(
                Grad(bba, xf, yf - 1.0, zf - 1.0),
                Grad(bbb, xf - 1.0, yf - 1.0, zf - 1.0),
                u
            );

            double y1 = DMath.Lerp(x1, x2, v);
            double y2 = DMath.Lerp(x3, x4, v);

            return DMath.Lerp(y1, y2, w);
        }

        /// <summary>
        /// Генерирует 2D шум с использованием дробного броуновского движения (fBm)
        /// </summary>
        public static double Fbm2D(double x, double y, int octaves = 6, double lacunarity = 2.0, double gain = 0.5)
        {
            double value = 0.0;
            double amplitude = 1.0;
            double frequency = 1.0;
            double maxValue = 0.0;

            for (int i = 0; i < octaves; i++)
            {
                value += amplitude * Noise2D(x * frequency, y * frequency);
                maxValue += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
            }

            return value / maxValue;
        }

        /// <summary>
        /// Генерирует 3D шум с использованием дробного броуновского движения (fBm)
        /// </summary>
        public static double Fbm3D(double x, double y, double z, int octaves = 6, double lacunarity = 2.0, double gain = 0.5)
        {
            double value = 0.0;
            double amplitude = 1.0;
            double frequency = 1.0;
            double maxValue = 0.0;

            for (int i = 0; i < octaves; i++)
            {
                value += amplitude * Noise3D(x * frequency, y * frequency, z * frequency);
                maxValue += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
            }

            return value / maxValue;
        }

        /// <summary>
        /// Сглаживающая функция: 6t^5 - 15t^4 + 10t^3
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        /// <summary>
        /// Вычисление градиента для 2D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Grad(int hash, double x, double y, double z)
        {
            var grad = Gradients[hash & 255];
            return grad.X * x + grad.Y * y + grad.Z * z;
        }

        /// <summary>
        /// Генерирует 2D карту шума
        /// </summary>
        public static double[,] GenerateNoiseMap2D(int width, int height, double scale = 1.0, DVector2 offset = default)
        {
            var map = new double[width, height];
            double maxVal = double.MinValue;
            double minVal = double.MaxValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double nx = (x + offset.X) * scale;
                    double ny = (y + offset.Y) * scale;
                    map[x, y] = Noise2D(nx, ny);

                    if (map[x, y] > maxVal) maxVal = map[x, y];
                    if (map[x, y] < minVal) minVal = map[x, y];
                }
            }

            // Нормализация
            double range = maxVal - minVal;
            if (range > 0.0)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        map[x, y] = (map[x, y] - minVal) / range;
            }

            return map;
        }

        /// <summary>
        /// Генерирует 2D карту шума с использованием fBm
        /// </summary>
        public static double[,] GenerateFbmMap2D(int width, int height, double scale = 1.0, DVector2 offset = default,
            int octaves = 6, double lacunarity = 2.0, double gain = 0.5)
        {
            var map = new double[width, height];
            double maxVal = double.MinValue;
            double minVal = double.MaxValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double nx = (x + offset.X) * scale;
                    double ny = (y + offset.Y) * scale;
                    map[x, y] = Fbm2D(nx, ny, octaves, lacunarity, gain);

                    if (map[x, y] > maxVal) maxVal = map[x, y];
                    if (map[x, y] < minVal) minVal = map[x, y];
                }
            }

            double range = maxVal - minVal;
            if (range > 0.0)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        map[x, y] = (map[x, y] - minVal) / range;
            }

            return map;
        }
    }
}