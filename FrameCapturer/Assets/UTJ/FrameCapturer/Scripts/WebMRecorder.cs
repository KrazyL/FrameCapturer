﻿using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UTJ
{
    [AddComponentMenu("UTJ/FrameCapturer/WebMRecorder")]
    [RequireComponent(typeof(Camera))]
    public class WebMRecorder : MovieRecorderBase
    {
        fcAPI.fcWebMContext m_ctx;
        fcAPI.fcWebMConfig m_webmconf = fcAPI.fcWebMConfig.default_value;
        fcAPI.fcStream m_ostream;
        int m_callback;
        int m_numVideoFrames;


        void InitializeContext()
        {
            m_numVideoFrames = 0;

            // initialize scratch buffer
            var cam = GetComponent<Camera>();
            UpdateScratchBuffer(cam.pixelWidth, cam.pixelHeight);

            // initialize context and stream
            {
                m_webmconf = fcAPI.fcWebMConfig.default_value;
                m_webmconf.video = m_captureVideo;
                m_webmconf.audio = m_captureAudio;
                m_webmconf.video_width = m_scratchBuffer.width;
                m_webmconf.video_height = m_scratchBuffer.height;
                m_webmconf.video_target_framerate = 60;
                m_webmconf.video_target_bitrate = m_videoBitrate;
                m_webmconf.audio_target_bitrate = m_audioBitrate;
                m_webmconf.audio_sample_rate = AudioSettings.outputSampleRate;
                m_webmconf.audio_num_channels = fcAPI.fcGetNumAudioChannels();
                m_ctx = fcAPI.fcWebMCreateContext(ref m_webmconf);

                m_outputPath = DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".webm";
                m_ostream = fcAPI.fcCreateFileStream(m_outputPath);
                fcAPI.fcWebMAddOutputStream(m_ctx, m_ostream);
            }

            // initialize command buffer
            {
                int tid = Shader.PropertyToID("_TmpFrameBuffer");
                m_cb = new CommandBuffer();
                m_cb.name = "WebMRecorder: copy frame buffer";
                m_cb.GetTemporaryRT(tid, -1, -1, 0, FilterMode.Bilinear);
                m_cb.Blit(BuiltinRenderTextureType.CurrentActive, tid);
                m_cb.SetRenderTarget(m_scratchBuffer);
                m_cb.DrawMesh(m_quad, Matrix4x4.identity, m_matCopy, 0, 0);
                m_cb.ReleaseTemporaryRT(tid);
            }
        }

        void ReleaseContext()
        {
            if(m_cb != null)
            {
                m_cb.Release();
                m_cb = null;
            }

            // scratch buffer is kept

            fcAPI.fcGuard(() =>
            {
                fcAPI.fcEraseDeferredCall(m_callback);
                m_callback = 0;

                if (m_ctx)
                {
                    fcAPI.fcWebMDestroyContext(m_ctx);
                    m_ctx.ptr = IntPtr.Zero;
                }
                if (m_ostream)
                {
                    fcAPI.fcDestroyStream(m_ostream);
                    m_ostream.ptr = IntPtr.Zero;
                }
            });
        }

        
        public override bool BeginRecording()
        {
            if (m_recording) { return false; }
            m_recording = true;

            InitializeContext();
            GetComponent<Camera>().AddCommandBuffer(CameraEvent.AfterEverything, m_cb);
            Debug.Log("WebMRecorder.BeginRecording(): " + outputPath);
            return true;
        }

        public override bool EndRecording()
        {
            if (!m_recording) { return false; }
            m_recording = false;

            GetComponent<Camera>().RemoveCommandBuffer(CameraEvent.AfterEverything, m_cb);
            ReleaseContext();
            Debug.Log("WebMRecorder.EndRecording(): " + outputPath);
            return true;
        }


        void OnAudioFilterRead(float[] samples, int channels)
        {
            if (m_recording && m_captureAudio)
            {
                if (channels != m_webmconf.audio_num_channels)
                {
                    Debug.LogError("WebMRecorder: audio channels mismatch!");
                    return;
                }
                fcAPI.fcWebMAddAudioFrame(m_ctx, samples, samples.Length);
            }
        }

        IEnumerator OnPostRender()
        {
            if (m_recording && m_captureVideo && Time.frameCount % m_captureEveryNthFrame == 0)
            {
                yield return new WaitForEndOfFrame();

                double timestamp = Time.unscaledTime;
                if (m_frameRateMode == FrameRateMode.Constant)
                {
                    timestamp = 1.0 / m_framerate * m_numVideoFrames;
                }

                m_callback = fcAPI.fcWebMAddVideoFrameTexture(m_ctx, m_scratchBuffer, timestamp, m_callback);
                GL.IssuePluginEvent(fcAPI.fcGetRenderEventFunc(), m_callback);
                m_numVideoFrames++;
            }
        }
    }

}