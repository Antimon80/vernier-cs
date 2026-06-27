using System.Buffers.Binary;
using Backend.Devices.GoDirect;
using Backend.Transport;

namespace Backend.Protocol
{
    /// <summary>
    /// Byte-level protocol implementation for Vernier GoDirect spectrometers.
    /// </summary>
    public sealed class SpectrometerProtocol
    {
        private const int HidPayloadLength = 64;

        private readonly ITransport _transport;
        private readonly SpectrometerModel _model;

        public SpectrometerProtocol(ITransport transport, SpectrometerModel model)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _model = model;
        }

        public SpectrometerModel Model => _model;

        // Public protocol methods

        /// <summary>
        /// Sends the initial protocol wake-up command.
        ///
        /// This must be the first command sent after opening the transport.
        /// The device does not return a response for this command.
        /// </summary>
        public Task WakeUp(CancellationToken ct = default)
        {
            return SendCommand(0x00, 0x00, 0x00, ct);
        }

        public async Task<ushort> GetModelCode(CancellationToken ct = default)
        {
            byte[] reply = await SendAndReadSingle(0x01, 0x00, 0x00, ct).ConfigureAwait(false);
            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(0, 2));
            return code;
        }

        public async Task<byte[]> ReadLinearitySequence(CancellationToken ct = default)
        {
            _transport.FlushInputBuffer();
            int packetCount = (_model.Pid == 0x0006) ? 128 : 56;

            await SendCommand(0x02, 0x00, 0x00, ct).ConfigureAwait(false);

            return await ReadPayloadBytes(packetCount, ct).ConfigureAwait(false);

        }

        public async Task<int> SetIntegrationTime(int ms, CancellationToken ct = default)
        {
            if (ms < 0 || ms > 0x3E8)
            {
                throw new ArgumentOutOfRangeException(nameof(ms));
            }

            byte lo = (byte)(ms & 0xFF);
            byte hi = (byte)((ms >> 8) & 0xFF);

            byte[] replay = await SendAndReadSingle(0x04, lo, hi, ct).ConfigureAwait(false);

            ushort echoed = BinaryPrimitives.ReadUInt16LittleEndian(replay.AsSpan(0, 2));
            return (int)echoed;
        }

        public async Task<bool> SetLamp(LampMode mode, bool on, CancellationToken ct = default)
        {
            byte command;

            if (mode == LampMode.White)
            {
                command = 0x41;
            }
            else if (mode == LampMode.Fluo405)
            {
                command = 0x42;
            }
            else if (mode == LampMode.Fluo500)
            {
                command = 0x43;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            byte value = on ? (byte)0x01 : (byte)0x00;

            byte[] reply = await SendAndReadSingle(command, value, 0x00, ct).ConfigureAwait(false);

            if (reply[1] != 0x00)
            {
                throw new InvalidOperationException($"No lamp echo received.");
            }

            return on;
        }

        public async Task<ushort[]> AcquireRawCounts(CancellationToken ct = default)
        {
            await SendCommand(0x40, 0x00, 0x00, ct).ConfigureAwait(false);

            byte[] bytes = await ReadPayloadBytes(_model.PacketCount, ct).ConfigureAwait(false);
            ushort[] values = DecodeU16LittleEndian(bytes);

            return values;
        }

        // Low-level helpers

        public Task SendCommand(byte b0, byte b1, byte b2, CancellationToken ct = default)
        {
            byte[] payload = new byte[HidPayloadLength];

            payload[0] = b0;
            payload[1] = b1;
            payload[2] = b2;

            return _transport.Write(payload, ct);
        }

        private async Task<byte[]> SendAndReadSingle(byte b0, byte b1, byte b2, CancellationToken ct)
        {
            try
            {
                await SendCommand(b0, b1, b2, ct).ConfigureAwait(false);

                byte[] packet = await _transport.Read(ct).ConfigureAwait(false);

                return SliceToModelPayload(packet);
            }
            catch (Exception)
            {
                _transport.FlushInputBuffer();
                throw;
            }
        }

        private async Task<byte[]> ReadPayloadBytes(int packetCount, CancellationToken ct)
        {
            int payloadBytes = _model.PacketPayloadBytes;

            if (payloadBytes <= 0 || payloadBytes > HidPayloadLength)
            {
                throw new InvalidOperationException("Invalid PacketPayloadBytes.");
            }

            byte[] result = new byte[packetCount * payloadBytes];
            int offset = 0;

            int received = 0;
            try
            {
                for (; received < packetCount; received++)
                {
                    ct.ThrowIfCancellationRequested();

                    byte[] packet = await _transport.Read(ct).ConfigureAwait(false);

                    Buffer.BlockCopy(packet, 0, result, offset, payloadBytes);
                    offset += payloadBytes;
                }

                return result;
            }
            catch (Exception)
            {
                int remaining = packetCount - received;
                if (remaining > 0)
                {
                    try
                    {
                        await _transport.Drain(remaining, perPacketTimeoutMs: 50, ct: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {

                    }
                }
                _transport.FlushInputBuffer();
                throw;
            }

        }

        private byte[] SliceToModelPayload(byte[] packet)
        {
            int payloadBytes = _model.PacketPayloadBytes;
            if (payloadBytes == HidPayloadLength)
            {
                return packet;
            }

            byte[] sliced = new byte[payloadBytes];
            Buffer.BlockCopy(packet, 0, sliced, 0, payloadBytes);
            return sliced;
        }

        // Decode helpers

        public static ushort[] DecodeU16BigEndian(byte[] bytes)
        {
            if (bytes.Length % 2 != 0)
            {
                throw new ArgumentException("Byte length must be even.", nameof(bytes));
            }

            int count = bytes.Length / 2;
            ushort[] values = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                values[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 2, 2));
            }

            return values;
        }

        public static ushort[] DecodeU16LittleEndian(byte[] bytes)
        {
            if (bytes.Length % 2 != 0)
            {
                throw new ArgumentException("Byte length must be even.", nameof(bytes));
            }

            int count = bytes.Length / 2;
            ushort[] values = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
            }

            return values;
        }
    }
}