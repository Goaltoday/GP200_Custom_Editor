namespace Gp200.Replica;

internal static class Crc32
{
    internal static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((0u - (crc & 1)) & 0xedb88320u);
        }
        return ~crc;
    }
}
