﻿using System;
using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;

namespace SmartGlass.Nano.FFmpeg
{
    public unsafe abstract class FFmpegBase : IDisposable
    {
        public bool IsDecoder
        {
            get
            {
                return (pCodec != null && ffmpeg.av_codec_is_decoder(pCodec) > 0);
            }
        }
        public bool IsEncoder
        {
            get
            {
                return (pCodec != null && ffmpeg.av_codec_is_encoder(pCodec) > 0);
            }
        }
        public bool IsVideo
        {
            get;
            internal set;
        }
        public bool IsAudio
        {
            get;
            internal set;
        }
        public bool Initialized
        {
            get;
            internal set;
        }
        public bool ContextCreated
        {
            get;
            internal set;
        }
        internal bool doResample = false;
        internal AVCodecID avCodecID;

        internal AVCodec* pCodec;
        internal AVCodecContext* pCodecContext;
        internal SwrContext* pResampler;
        internal AVFrame* pDecodedFrame;
        internal AVPacket* pPacket;


        public FFmpegBase(bool video = false, bool audio = false)
        {
            this.Initialized = false;
            this.ContextCreated = false;
            this.IsAudio = false;
            this.IsVideo = false;

            if (video && audio)
            {
                throw new InvalidProgramException("FFmpeg De-/Encoder cannot be both, audio and video");
            }
            else if (video)
            {
                this.IsVideo = true;
            }
            else if (audio)
            {
                this.IsAudio = true;
            }
            else
            {
                throw new InvalidProgramException("FFmpeg not created with info wether audio or video");
            }
        }

        /// <summary>
        /// Sets the codec context parameters.
        /// </summary>
        internal abstract void SetCodecContextParams();

        /// <summary>
        /// Sets the resampler parameters.
        /// </summary>
        internal abstract void SetResamplerParams();

        /// <summary>
        /// Inits the Codec context.
        /// </summary>
        /// <param name="encoder">If set to <c>true</c> encoder.</param>
        void CreateContext(bool encoder = false)
        {
            if (!Initialized)
            {
                throw new InvalidOperationException("Instance is not initialized yet, call Initialize() first");
            }
            else if (ContextCreated)
            {
                throw new InvalidOperationException("Context already initialized!");
            }

            // ffmpeg.av_register_all();
            if (encoder)
                pCodec = ffmpeg.avcodec_find_encoder(avCodecID);
            else
                pCodec = ffmpeg.avcodec_find_decoder(avCodecID);

            if (pCodec == null)
            {
                throw new InvalidOperationException("VideoCodec not found");
            }
            pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
            if (pCodecContext == null)
            {
                throw new InvalidOperationException("Could not allocate codec context");
            }

            // Call to abstract method
            SetCodecContextParams();

            if ((pCodec->capabilities & ffmpeg.AV_CODEC_CAP_TRUNCATED) == ffmpeg.AV_CODEC_CAP_TRUNCATED)
                pCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_TRUNCATED;

            if (ffmpeg.avcodec_open2(pCodecContext, pCodec, null) < 0)
            {
                throw new InvalidOperationException("Could not open codec");
            }

            if (doResample)
            {
                pResampler = ffmpeg.swr_alloc();
                // Call to abstract method
                SetResamplerParams();

                // Initialize resampler
                ffmpeg.swr_init(pResampler);
                if (ffmpeg.swr_is_initialized(pResampler) <= 0)
                {
                    throw new InvalidOperationException("Failed to init resampler");
                }
            }

            pDecodedFrame = ffmpeg.av_frame_alloc();
            pPacket = ffmpeg.av_packet_alloc();
            //ffmpeg.av_init_packet(this.pPacket);
            ContextCreated = true;
        }

        /// <summary>
        /// Initializes the Codec context as decoder.
        /// </summary>
        public void CreateDecoderContext()
        {
            CreateContext(encoder: false);
        }

        /// <summary>
        /// Initializes the Codec context as encoder.
        /// </summary>
        public void CreateEncoderContext()
        {
            CreateContext(encoder: true);
        }

        /// <summary>
        /// Flush all buffers of CodecContext -> use when changing video quality (on marker packet)
        /// </summary>
        public void Reinit()
        {
            if (!Initialized || !ContextCreated)
            {
                throw new InvalidOperationException("Cannot reinit uninitialized context");
            }
            ffmpeg.avcodec_flush_buffers(pCodecContext);
        }

        /// <summary>
        /// Send an encoded packet / frame in ffmpeg decoding queue.
        /// </summary>
        /// <returns>Return value of avcodec_send_packet: 0 on success, -1 on failure</returns>
        /// <param name="data">Encoded Data blob</param>
        internal int EnqueuePacketForDecoding(byte[] data)
        {
            if (!this.IsDecoder)
            {
                Console.WriteLine("QueuePacketForDecoding: Context is not initialized for decoding");
                return -1;
            }
            int ret;
            fixed (byte* pData = data)
            {
                pPacket->data = pData;
                pPacket->size = data.Length;

                ret = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
            }
            if (ret < 0)
            {
                Console.WriteLine($"Error: Code: {ret}, Msg:{FFmpegHelper.av_strerror(ret)}");
            }

            ffmpeg.av_packet_unref(pPacket);
            return 0;
        }

        public abstract Thread DecodingThread();

        /*
         * TODO: Encoding stuff
        void EncodeFrameInternal(AVFrame* frame)
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            if (ffmpeg.avcodec_send_frame(pCodecContext, frame) < 0)
            {
                throw new InvalidProgramException("EncodeFrame: Could not send frame");
            }
            int ret = ffmpeg.avcodec_receive_packet(pCodecContext, packet);
            if (ret == -11)
            {
                throw new InvalidProgramException("EncodeFrame: EOF");
            }
            if (ret < 0)
            {
                throw new InvalidOperationException("EncodeFrame: Could not receive packet");
            }

            ffmpeg.av_packet_free(&packet);
        }

        public void EncodeFrame(byte[] data, int size)
        {
            AVFrame* frame = ffmpeg.av_frame_alloc();

            fixed (byte* pData = data)
            {
                //FIXME
                //frame->extended_data = pData;
                frame->pkt_size = size;

                EncodeFrameInternal(frame);

                ffmpeg.av_frame_free(&frame);
            }
        }
        */

        public void Dispose()
        {
            ffmpeg.avcodec_close(pCodecContext);
            ffmpeg.av_free(pCodecContext);
            ffmpeg.av_free(pCodec);
            //ffmpeg.av_packet_free(pPacket);
            //ffmpeg.av_frame_free(pDecodedFrame);
        }
    }
}
