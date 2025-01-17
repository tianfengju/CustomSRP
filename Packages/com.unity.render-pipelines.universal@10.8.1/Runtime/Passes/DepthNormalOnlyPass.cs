using System;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class DepthNormalOnlyPass : ScriptableRenderPass
    {
        internal RenderTextureDescriptor normalDescriptor { get; private set; }
        internal RenderTextureDescriptor depthDescriptor { get; private set; }

        private RenderTargetIdentifier depthHandle { get; set; }
        private RenderTargetHandle normalHandle { get; set; }
        private ShaderTagId m_ShaderTagId = new ShaderTagId("DepthNormals");
        private FilteringSettings m_FilteringSettings;

        // Constants
        private const int k_DepthBufferBits = 32;

        /// <summary>
        /// Create the DepthNormalOnlyPass
        /// </summary>
        public DepthNormalOnlyPass(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DepthNormalOnlyPass));
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            renderPassEvent = evt;
        }

        /// <summary>
        /// Configure the pass
        /// </summary>
        public void Setup(RenderTextureDescriptor baseDescriptor, RenderTargetIdentifier depthHandle, RenderTargetHandle normalHandle)
        {
            this.depthHandle = depthHandle;
            baseDescriptor.width = baseDescriptor.width >> UniversalRenderPipeline.asset.DepthDownsampling;
            baseDescriptor.height = baseDescriptor.height >> UniversalRenderPipeline.asset.DepthDownsampling;
            baseDescriptor.colorFormat = RenderTextureFormat.Depth;
            baseDescriptor.depthBufferBits = k_DepthBufferBits;
            baseDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
            depthDescriptor = baseDescriptor;

            this.normalHandle = normalHandle;
            baseDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            baseDescriptor.depthBufferBits = 0;
            baseDescriptor.msaaSamples = 1;
            normalDescriptor = baseDescriptor;
        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.GetTemporaryRT(normalHandle.id, normalDescriptor, FilterMode.Point);
            cmd.GetTemporaryRT(ShaderDefine.CAMERA_DEPTH_TEXTURE, depthDescriptor, FilterMode.Point);
            // ConfigureTarget(
            //     new RenderTargetIdentifier(normalHandle.Identifier(), 0, CubemapFace.Unknown, -1),
            //     new RenderTargetIdentifier(depthHandle.Identifier(), 0, CubemapFace.Unknown, -1)
            //     );
            ConfigureTarget(
                new RenderTargetIdentifier(normalHandle.Identifier(), 0, CubemapFace.Unknown, -1),
                depthHandle
            );
            //原來是Color.Black 改为new Color(0, 0, 1, 0) 才能获得正确的天空盒深度
            ConfigureClear(ClearFlag.All, new Color(0, 0, 1, 0));
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // NOTE: Do NOT mix ProfilingScope with named CommandBuffers i.e. CommandBufferPool.Get("name").
            // Currently there's an issue which results in mismatched markers.
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.DepthNormalPrepass)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = CreateDrawingSettings(m_ShaderTagId, ref renderingData, sortFlags);
                drawSettings.perObjectData = PerObjectData.None;

                ref CameraData cameraData = ref renderingData.cameraData;
                Camera camera = cameraData.camera;

                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException("cmd");
            }

            if (depthHandle != RenderTargetHandle.CameraTarget.Identifier())
            {
                cmd.ReleaseTemporaryRT(normalHandle.id);
                cmd.ReleaseTemporaryRT(ShaderDefine.CAMERA_DEPTH_TEXTURE);
                normalHandle = RenderTargetHandle.CameraTarget;
                depthHandle = RenderTargetHandle.CameraTarget.Identifier();
            }
        }
    }
}
