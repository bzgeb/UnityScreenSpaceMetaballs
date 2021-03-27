using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RenderScreenSpaceMetaballs : ScriptableRendererFeature
{
    #region Render Objects

    class RenderObjectsPass : ScriptableRenderPass
    {
        readonly int _renderTargetId;
        readonly ProfilingSampler _profilingSampler;
        readonly List<ShaderTagId> _shaderTagIds = new List<ShaderTagId>();

        RenderTargetIdentifier _renderTargetIdentifier;
        FilteringSettings _filteringSettings;
        RenderStateBlock _renderStateBlock;

        public RenderObjectsPass(string profilerTag, int renderTargetId, LayerMask layerMask)
        {
            _profilingSampler = new ProfilingSampler(profilerTag);
            _renderTargetId = renderTargetId;

            _filteringSettings = new FilteringSettings(null, layerMask);

            _shaderTagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
            _shaderTagIds.Add(new ShaderTagId("UniversalForward"));
            _shaderTagIds.Add(new ShaderTagId("UniversalForwardOnly"));
            _shaderTagIds.Add(new ShaderTagId("LightweightForward"));

            _renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor blitTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            blitTargetDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            cmd.GetTemporaryRT(_renderTargetId, blitTargetDescriptor);
            _renderTargetIdentifier = new RenderTargetIdentifier(_renderTargetId);

            ConfigureTarget(_renderTargetIdentifier, renderingData.cameraData.renderer.cameraDepthTarget);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = SortingCriteria.CommonOpaque;
            DrawingSettings drawingSettings =
                CreateDrawingSettings(_shaderTagIds, ref renderingData, sortingCriteria);

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filteringSettings,
                    ref _renderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    #endregion

    #region Kawase Blur

    class KawaseBlurRenderPass : ScriptableRenderPass
    {
        public Material BlurMaterial;
        public Material BlitMaterial;
        public int Passes;
        public int Downsample;

        int _tmpId1;
        int _tmpId2;

        RenderTargetIdentifier _tmpRT1;
        RenderTargetIdentifier _tmpRT2;

        readonly int _blurSourceId;
        RenderTargetIdentifier _blurSourceIdentifier;

        readonly ProfilingSampler _profilingSampler;

        public KawaseBlurRenderPass(string profilerTag, int blurSourceId)
        {
            _profilingSampler = new ProfilingSampler(profilerTag);
            _blurSourceId = blurSourceId;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _blurSourceIdentifier = new RenderTargetIdentifier(_blurSourceId);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var width = cameraTextureDescriptor.width / Downsample;
            var height = cameraTextureDescriptor.height / Downsample;

            _tmpId1 = Shader.PropertyToID("tmpBlurRT1");
            _tmpId2 = Shader.PropertyToID("tmpBlurRT2");
            cmd.GetTemporaryRT(_tmpId1, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cmd.GetTemporaryRT(_tmpId2, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);

            _tmpRT1 = new RenderTargetIdentifier(_tmpId1);
            _tmpRT2 = new RenderTargetIdentifier(_tmpId2);

            ConfigureTarget(_tmpRT1);
            ConfigureTarget(_tmpRT2);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RenderTextureDescriptor opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
            opaqueDesc.depthBufferBits = 0;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // first pass
                cmd.SetGlobalFloat("_offset", 1.5f);
                cmd.Blit(_blurSourceIdentifier, _tmpRT1, BlurMaterial);

                for (var i = 1; i < Passes - 1; i++)
                {
                    cmd.SetGlobalFloat("_offset", 0.5f + i);
                    cmd.Blit(_tmpRT1, _tmpRT2, BlurMaterial);

                    // pingpong
                    var rttmp = _tmpRT1;
                    _tmpRT1 = _tmpRT2;
                    _tmpRT2 = rttmp;
                }

                // final pass
                cmd.SetGlobalFloat("_offset", 0.5f + Passes - 1f);
                cmd.Blit(_tmpRT1, renderingData.cameraData.renderer.cameraColorTarget, BlitMaterial);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(_tmpId1);
            cmd.ReleaseTemporaryRT(_tmpId2);
        }
    }

    #endregion

    #region Renderer Feature

    RenderObjectsPass _renderObjectsPass;
    KawaseBlurRenderPass _blurPass;

    const string PassTag = "RenderScreenSpaceMetaballs";
    [SerializeField] string _renderTargetId = "_RenderMetaballsRT";
    [SerializeField] LayerMask _layerMask;
    [SerializeField] Material _blurMaterial;
    [SerializeField] Material _blitMaterial;
    [SerializeField, Range(1, 16)] int _blurPasses = 1;

    public override void Create()
    {
        int renderTargetId = Shader.PropertyToID(_renderTargetId);
        _renderObjectsPass = new RenderObjectsPass(PassTag, renderTargetId, _layerMask);

        _blurPass = new KawaseBlurRenderPass("KawaseBlur", renderTargetId)
        {
            Downsample = 1,
            Passes = _blurPasses,
            BlitMaterial = _blitMaterial,
            BlurMaterial = _blurMaterial
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_renderObjectsPass);
        renderer.EnqueuePass(_blurPass);
    }

    #endregion
}