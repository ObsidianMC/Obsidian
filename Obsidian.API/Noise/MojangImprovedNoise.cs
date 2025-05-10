using System;
using System.Text;

namespace Obsidian.API.Noise;


public sealed class ImprovedNoise
{
    private const float SHIFT_UP_EPSILON = 1.0e-7f;
    private readonly byte[] p;
    public readonly double xo;
    public readonly double yo;
    public readonly double zo;

    public ImprovedNoise(Random random)
    {
        xo = random.NextDouble() * 256.0;
        yo = random.NextDouble() * 256.0;
        zo = random.NextDouble() * 256.0;
        p = new byte[256];

        for (int k = 0; k < 256; k++)
        {
            p[k] = (byte)k;
        }

        for (int k = 0; k < 256; k++)
        {
            int j = random.Next(256 - k);
            byte temp = p[k];
            p[k] = p[k + j];
            p[k + j] = temp;
        }
    }

    public double Noise(double x, double y, double z)
    {
        return Noise(x, y, z, 0.0, 0.0);
    }

    [Obsolete]
    public double Noise(double x, double y, double z, double delta, double scale)
    {
        double nx = x + xo;
        double ny = y + yo;
        double nz = z + zo;

        int ix = MathUtils.Floor(nx);
        int iy = MathUtils.Floor(ny);
        int iz = MathUtils.Floor(nz);

        double fx = nx - ix;
        double fy = ny - iy;
        double fz = nz - iz;

        double d6 = 0.0;
        if (delta != 0.0)
        {
            double d7 = scale >= 0.0 && scale < fy ? scale : fy;
            d6 = Math.Floor(d7 / delta + 1.0000000116860974E-7) * delta;
        }

        return SampleAndLerp(ix, iy, iz, fx, fy - d6, fz, fy);
    }

    public double NoiseWithDerivative(double x, double y, double z, double[] derivatives)
    {
        double nx = x + xo;
        double ny = y + yo;
        double nz = z + zo;

        int ix = MathUtils.Floor(nx);
        int iy = MathUtils.Floor(ny);
        int iz = MathUtils.Floor(nz);

        double fx = nx - ix;
        double fy = ny - iy;
        double fz = nz - iz;

        return SampleWithDerivative(ix, iy, iz, fx, fy, fz, derivatives);
    }

    private static double GradDot(int hash, double x, double y, double z)
    {
        return SimplexNoise.Dot(SimplexNoise.GRADIENT[hash & 15], x, y, z);
    }

    private int P(int index)
    {
        return p[index & 255] & 255;
    }

    private double SampleAndLerp(int ix, int iy, int iz, double fx, double fy, double fz, double fyOriginal)
    {
        int i = P(ix);
        int j = P(ix + 1);
        int k = P(i + iy);
        int l = P(i + iy + 1);
        int i1 = P(j + iy);
        int j1 = P(j + iy + 1);

//double[] g = SimplexNoise.GRADIENT;

        double v0 = GradDot(P(k + iz), fx, fy, fz);
        double v1 = GradDot(P(i1 + iz), fx - 1.0, fy, fz);
        double v2 = GradDot(P(l + iz), fx, fy - 1.0, fz);
        double v3 = GradDot(P(j1 + iz), fx - 1.0, fy - 1.0, fz);

        double v4 = GradDot(P(k + iz + 1), fx, fy, fz - 1.0);
        double v5 = GradDot(P(i1 + iz + 1), fx - 1.0, fy, fz - 1.0);
        double v6 = GradDot(P(l + iz + 1), fx, fy - 1.0, fz - 1.0);
        double v7 = GradDot(P(j1 + iz + 1), fx - 1.0, fy - 1.0, fz - 1.0);

        double u = MathUtils.Smoothstep(fx);
        double v = MathUtils.Smoothstep(fyOriginal);
        double w = MathUtils.Smoothstep(fz);

        return MathUtils.Lerp3(u, v, w, v0, v1, v2, v3, v4, v5, v6, v7);
    }

    private double SampleWithDerivative(int ix, int iy, int iz, double fx, double fy, double fz, double[] derivatives)
    {
        // Implementation analogous to SampleAndLerp with derivative computation.
        // Fill in based on translated helper methods as required.
        throw new NotImplementedException("This method has not been fully translated.");
    }

}
