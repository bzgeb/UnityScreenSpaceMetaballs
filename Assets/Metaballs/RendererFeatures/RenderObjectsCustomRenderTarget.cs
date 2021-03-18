using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RenderObjectsCustomRenderTarget : ScriptableRendererFeature
{
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
            cmd.GetTemporaryRT(_renderTargetId, blitTargetDescriptor);
            _renderTargetIdentifier = new RenderTargetIdentifier(_renderTargetId);
            ConfigureTarget(_renderTargetIdentifier);
            ConfigureClear(ClearFlag.All, Color.clear);
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

    RenderObjectsPass _renderObjectsPass;

    const string PassTag = "RenderObjectsCustomRenderTarget";
    [SerializeField] string _renderTargetId;
    [SerializeField] LayerMask _layerMask;

    public override void Create()
    {
        int renderTargetId = Shader.PropertyToID(_renderTargetId);
        _renderObjectsPass = new RenderObjectsPass(PassTag, renderTargetId, _layerMask);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_renderObjectsPass);
    }
}