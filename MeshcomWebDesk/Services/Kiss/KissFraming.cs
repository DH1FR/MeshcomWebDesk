namespace MeshcomWebDesk.Services.Kiss;

/// <summary>
/// KISS (SLIP-style) framing over a TCP byte stream, per
/// <c>docs/kiss-mode-analysis.md</c> §3.4 and the firmware
/// <c>kiss_tcp_protocol.md</c>.
///
/// <para>Frame on the wire: <c>FEND | type | data(escaped) | FEND</c>.
/// <c>type = (port &lt;&lt; 4) | command</c>; command is always 0 here.</para>
/// </summary>
public static class KissFraming
{
    public const byte FEND  = 0xC0;
    public const byte FESC  = 0xDB;
    public const byte TFEND = 0xDC;
    public const byte TFESC = 0xDD;

    /// <summary>Data frame, port 0 – AX.25 UI frame (both directions).</summary>
    public const byte TypeData = 0x00;

    /// <summary>RxMeta frame, port 1 – snr:int8, rssi:int16 LE (only with <c>--kiss meta on</c>).</summary>
    public const byte TypeRxMeta = 0x10;

    /// <summary>
    /// SrcInfo frame, port 2 – full origin callsign (ASCII, incl. real SSID), node → client.
    /// Sent immediately <b>before</b> the <c>0x00</c> data frame, and only when the origin's
    /// SSID &gt; 15 had to be clamped to <c>-15</c> in the AX.25 <c>src</c> field. Use it as the
    /// true source of the following data frame (display + reply addressee).
    /// </summary>
    public const byte TypeSrcInfo = 0x20;

    /// <summary>TX-result frame, port 15 – status:int8 [+ msg_id:uint32 LE] (firmware v1.2+, node → client).</summary>
    public const byte TypeTxResult = 0xF0;

    /// <summary>
    /// Wraps <paramref name="payload"/> in a KISS frame of the given <paramref name="type"/>
    /// with SLIP escaping and leading/trailing FEND.
    /// </summary>
    public static byte[] Frame(byte type, ReadOnlySpan<byte> payload)
    {
        var o = new List<byte>(payload.Length + 4) { FEND };
        AppendEscaped(o, type);
        foreach (var b in payload)
            AppendEscaped(o, b);
        o.Add(FEND);
        return [.. o];
    }

    private static void AppendEscaped(List<byte> o, byte b)
    {
        switch (b)
        {
            case FEND: o.Add(FESC); o.Add(TFEND); break;
            case FESC: o.Add(FESC); o.Add(TFESC); break;
            default:   o.Add(b);                   break;
        }
    }
}

/// <summary>
/// Stateful KISS deframer: feed it whatever bytes arrive from the socket, get back
/// complete, un-escaped frames (each including the leading type byte at index 0).
/// Not thread-safe – use one instance per connection, from its single reader loop.
/// </summary>
public sealed class KissDeframer
{
    private readonly List<byte> _buf = new(512);
    private bool _inEsc;

    /// <summary>
    /// Consumes <paramref name="data"/> and returns every frame that completed within it.
    /// Consecutive FENDs / empty frames are skipped. A partial trailing frame is retained
    /// for the next call.
    /// </summary>
    public List<byte[]> Push(ReadOnlySpan<byte> data)
    {
        var frames = new List<byte[]>();
        foreach (var b in data)
        {
            if (b == KissFraming.FEND)
            {
                if (_buf.Count > 0)
                    frames.Add([.. _buf]);
                _buf.Clear();
                _inEsc = false;
                continue;
            }

            if (_inEsc)
            {
                _buf.Add(b == KissFraming.TFEND ? KissFraming.FEND
                       : b == KissFraming.TFESC ? KissFraming.FESC
                       : b);
                _inEsc = false;
            }
            else if (b == KissFraming.FESC)
            {
                _inEsc = true;
            }
            else
            {
                _buf.Add(b);
            }
        }
        return frames;
    }
}
