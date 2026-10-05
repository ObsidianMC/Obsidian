namespace Obsidian.API;

public class Traverse
{
    private VectorD StartPoint { get; }
    private VectorD EndPoint { get; }

    /// <summary>
    /// Initializes instance of <see cref="Traverse"/> with given start and end points
    /// </summary>
    /// <param name="startPoint">Start point of traversal</param>
    /// <param name="endPoint">End point of traversal</param>

    public Traverse(VectorD startPoint, VectorD endPoint)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    private static double Frac0(double x)
    {
        return x - Math.Floor(x);
    }
    private static double Frac1(double x)
    {
        return 1 - x + Math.Floor(x);
    }

    /// <summary>
    /// Runs voxel traversing
    /// </summary>
    /// <returns>List of intersected positions</returns>
    public List<VectorD> Run()
    {

        List<VectorD> blocks = [];

        double tMaxX, tMaxY, tMaxZ, tDeltaX, tDeltaY, tDeltaZ;
        VectorD voxel = new VectorD();

        double x1 = StartPoint.X, y1 = StartPoint.Y, z1 = StartPoint.Z; // start point   
        double x2 = EndPoint.X, y2 = EndPoint.Y, z2 = EndPoint.Z; // end point   


        var dx = Math.Sign(x2 - x1); // x direction
        if (dx != 0) tDeltaX = Math.Min(dx / (x2 - x1), double.PositiveInfinity); else tDeltaX = double.PositiveInfinity;
        if (dx > 0) tMaxX = tDeltaX * Frac1(x1); else tMaxX = tDeltaX * Frac0(x1);
        voxel.X = x1;

        var dy = Math.Sign(y2 - y1); // y direction
        if (dy != 0) tDeltaY = Math.Min(dy / (y2 - y1), double.PositiveInfinity); else tDeltaY = double.PositiveInfinity;
        if (dy > 0) tMaxY = tDeltaY * Frac1(y1); else tMaxY = tDeltaY * Frac0(y1);
        voxel.Y = y1;

        var dz = Math.Sign(z2 - z1); // z direction
        if (dz != 0) tDeltaZ = Math.Min(dz / (z2 - z1), double.PositiveInfinity); else tDeltaZ = double.PositiveInfinity;
        if (dz > 0) tMaxZ = tDeltaZ * Frac1(z1); else tMaxZ = tDeltaZ * Frac0(z1);
        voxel.Z = z1;

        while (true)
        {
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    voxel.X += dx;
                    tMaxX += tDeltaX;
                }
                else
                {
                    voxel.Z += dz;
                    tMaxZ += tDeltaZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    voxel.Y += dy;
                    tMaxY += tDeltaY;
                }
                else
                {
                    voxel.Z += dz;
                    tMaxZ += tDeltaZ;
                }
            }
            if (tMaxX > 1 && tMaxY > 1 && tMaxZ > 1) break;
            blocks.Add(voxel);
        }
        return blocks;
    }

}
