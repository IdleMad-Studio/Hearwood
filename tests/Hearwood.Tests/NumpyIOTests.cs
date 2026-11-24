using Hearwood.Pocket;

namespace Hearwood.Tests;

// Ported from another project by the same author.
public class NumpyIOTests
{
    [Fact]
    public void WriteReadRoundtrip_PreservesDataAndShape()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npy-test-{Guid.NewGuid():N}.npy");
        try
        {
            var shape = new[] { 2, 3, 4 };
            var data = Enumerable.Range(0, 24).Select(i => i * 0.25f - 3f).ToArray();
            NumpyIO.Write(path, (float[])data.Clone(), shape);

            var (readData, readShape) = NumpyIO.Read(path);
            Assert.Equal(shape, readShape);
            Assert.Equal(data, readData);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ParsesShapeTupleWithCommas()
    {
        // The bos_before_voice.npy case: multi-dim shapes must not truncate at
        // the first comma inside the tuple (historical bug: shape became [1]).
        var path = Path.Combine(Path.GetTempPath(), $"npy-shape-{Guid.NewGuid():N}.npy");
        try
        {
            NumpyIO.Write(path, new float[8], [1, 1, 8]);
            var (_, readShape) = NumpyIO.Read(path);
            Assert.Equal(new[] { 1, 1, 8 }, readShape);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

