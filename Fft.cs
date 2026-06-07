namespace SpectrumViewerRT;

public static class Fft
{
    public static void Transform(double[] real, double[] imaginary)
    {
        int n = real.Length;
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2.0 * Math.PI / length;
            double wLengthReal = Math.Cos(angle);
            double wLengthImaginary = Math.Sin(angle);
            int half = length >> 1;

            for (int i = 0; i < n; i += length)
            {
                double wReal = 1;
                double wImaginary = 0;
                for (int k = 0; k < half; k++)
                {
                    int even = i + k;
                    int odd = even + half;
                    double oddReal = real[odd] * wReal - imaginary[odd] * wImaginary;
                    double oddImaginary = real[odd] * wImaginary + imaginary[odd] * wReal;

                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;

                    double nextReal = wReal * wLengthReal - wImaginary * wLengthImaginary;
                    wImaginary = wReal * wLengthImaginary + wImaginary * wLengthReal;
                    wReal = nextReal;
                }
            }
        }
    }
}
