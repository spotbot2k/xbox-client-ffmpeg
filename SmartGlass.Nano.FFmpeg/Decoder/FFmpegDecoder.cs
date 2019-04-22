using System;
using SDL2;
using SmartGlass.Common;
using SmartGlass.Nano.Consumer;
using SmartGlass.Nano.Packets;

using SmartGlass.Nano.FFmpeg.Renderer;
using SmartGlass.Nano.FFmpeg.Producer;
using SmartGlass.Nano.FFmpeg.Decoder;
using System.Collections.Generic;

namespace SmartGlass.Nano.FFmpeg.Decoder
{
    public class FFmpegDecoder : IDisposable
    {
        private bool _disposed = false;

        NanoClient _client;
        AudioFormat _audioFormat;
        VideoFormat _videoFormat;
        VideoAssembler _videoAssembler;

        bool _audioContextInitialized;
        bool _videoContextInitialized;

        DateTime _audioRefTimestamp;
        DateTime _videoRefTimestamp;

        uint _audioFrameId;
        uint _videoFrameId;

        FFmpegAudio _audioDecoder;
        FFmpegVideo _videoDecoder;

        public int DequeueDecodedVideoFrame(out byte[][] frameData, out int[] lineSizes)
        {
            return _videoDecoder.DequeueDecodedFrame(out frameData, out lineSizes);
        }

        public int DequeueDecodedAudioSample(out byte[] sampleData)
        {
            return _audioDecoder.DequeueDecodedFrame(out sampleData);
        }


        public FFmpegDecoder(NanoClient client, AudioFormat audioFormat, VideoFormat videoFormat)
        {
            _client = client;

            _videoAssembler = new VideoAssembler();

            _audioFormat = audioFormat;
            _videoFormat = videoFormat;

            _videoRefTimestamp = _client.Video.ReferenceTimestamp;
            _audioRefTimestamp = _client.Audio.ReferenceTimestamp;

            _audioFrameId = _client.Audio.FrameId;
            _videoFrameId = _client.Video.FrameId;

            _audioDecoder = new FFmpegAudio();
            _videoDecoder = new FFmpegVideo();

            _audioDecoder.Initialize(_audioFormat);
            _videoDecoder.Initialize(_videoFormat);
            _audioDecoder.CreateDecoderContext();
            _videoDecoder.CreateDecoderContext();
        }

        /* Called by NanoClient on freshly received data */
        public void DecodeAudioData(object sender, AudioDataEventArgs args)
        {
            // TODO: Sorting
            AACFrame frame = AudioAssembler.AssembleAudioFrame(
                data: args.AudioData,
                profile: AACProfile.LC,
                samplingFreq: (int)_audioFormat.SampleRate,
                channels: (byte)_audioFormat.Channels);

            if (frame == null)
                return;

            if (!_audioContextInitialized)
            {
                // Initialize decoder
                _audioDecoder.UpdateCodecParameters(frame.GetCodecSpecificData());
                _audioContextInitialized = true;
            }
            if (_audioContextInitialized)
                // Enqueue encoded audio data in decoder
                _audioDecoder.EnqueuePacketForDecoding(frame.GetSamplesWithHeader());
        }

        public void DecodeVideoData(object sender, VideoDataEventArgs args)
        {
            // TODO: Sorting
            var frame = _videoAssembler.AssembleVideoFrame(args.VideoData);

            if (frame == null)
                return;

            if (!_videoContextInitialized && frame.PrimaryType == NalUnitType.SEQUENCE_PARAMETER_SET)
            {
                // Initialize decoder with PPS & SPS
                _videoDecoder.UpdateCodecParameters(frame.GetCodecSpecificDataAvcc());
                _videoContextInitialized = true;
            }
            if (_videoContextInitialized)
                // Enqueue encoded video data in decoder
                _videoDecoder.EnqueuePacketForDecoding(frame.RawData);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _audioDecoder.Dispose();
                    _videoDecoder.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
