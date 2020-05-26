using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DXWPF;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Prime_MIDI
{
    class ImgScene : IDirect3D
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Vert
        {
            public Vector4 pos;
            public Vector2 uv;
            public float min;
            public float max;
            public float pow;
        }

        public virtual D3D11 Renderer
        {
            get { return context; }
            set
            {
                if (Renderer != null)
                {
                    Renderer.Rendering -= ContextRendering;
                    Detach();
                }
                context = value;
                if (Renderer != null)
                {
                    Renderer.Rendering += ContextRendering;
                    Attach();
                }
            }
        }
        D3D11 context;

        Buffer indexBuff;
        Buffer vertexBuff;
        InputLayout vertLayout;

        VertexShader vertShader;
        ShaderBytecode vertShaderCode;
        PixelShader fragShader;
        ShaderBytecode fragShaderCode;

        ShaderResourceView textureView;
        SamplerState samplerStateMap;
        float max;

        public VScale VerticalScale { get; set; } = new VScale() { Bottom = 127, Top = 0 };
        public HScale HorizontalScale { get; set; } = new HScale() { Left= 0, Right = 1 };

        public double ColorPow = 0.2;
        public double ScaledMax = 1;
        public double ScaledMin = 0;

        public ImgScene()
        {

        }

        void ContextRendering(object aCtx, DrawEventArgs args) { RenderScene(args); }

        protected void Attach()
        {
            if (Renderer == null)
                return;

            string imgShaderData;
            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames();
            using (var stream = assembly.GetManifestResourceStream("Prime_MIDI.shader.fx"))
            using (var reader = new System.IO.StreamReader(stream))
                imgShaderData = reader.ReadToEnd();

            vertShaderCode = ShaderBytecode.Compile(imgShaderData, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None);
            fragShaderCode = ShaderBytecode.Compile(imgShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None);
            vertShader = new VertexShader(Renderer.Device, vertShaderCode);
            fragShader = new PixelShader(Renderer.Device, fragShaderCode);

            vertLayout = new InputLayout(Renderer.Device, ShaderSignature.GetInputSignature(vertShaderCode), new[] {
                new InputElement("POSITION",0,SharpDX.DXGI.Format.R32G32B32A32_Float,0,0),
                new InputElement("UV",0,SharpDX.DXGI.Format.R32G32_Float,16,0),
                new InputElement("MIN",0,SharpDX.DXGI.Format.R32_Float,24,0),
                new InputElement("MAX",0,SharpDX.DXGI.Format.R32_Float,28,0),
                new InputElement("POW",0,SharpDX.DXGI.Format.R32_Float,32,0),
            });

            vertexBuff = new Buffer(Renderer.Device, new BufferDescription()
            {
                BindFlags = BindFlags.VertexBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                SizeInBytes = 36 * 6,
                Usage = ResourceUsage.Dynamic,
                StructureByteStride = 0
            });

            var samplerDescMap = new SamplerStateDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MipLodBias = 0,
                MaximumAnisotropy = 1,
                ComparisonFunction = Comparison.Always,
                BorderColor = Color.Transparent,
                MinimumLod = 0,
                MaximumLod = float.MaxValue
            };
            samplerStateMap = new SamplerState(Renderer.Device, samplerDescMap);
        }

        protected void Detach()
        {
        }

        public void LoadTexture(float[,] data)
        {
            var device = Renderer.Device;
            Texture2D tex;
            using (var buffer = new DataStream(data.Length * 4, true, true))
            {
                max = 0;
                for (int j = 0; j < data.GetLength(1); j++)
                    for (int i = 0; i < data.GetLength(0); i++)
                    {
                        if (data[i, j] > max) max = data[i, j];
                        buffer.Write(Math.Abs(data[i, j]));
                    }
                tex = new Texture2D(device, new Texture2DDescription()
                {
                    Width = data.GetLength(0),
                    Height = data.GetLength(1),
                    ArraySize = 1,
                    BindFlags = BindFlags.ShaderResource,
                    Usage = ResourceUsage.Default,
                    CpuAccessFlags = CpuAccessFlags.None,
                    Format = SharpDX.DXGI.Format.R32_Float,
                    MipLevels = 1,
                    OptionFlags = ResourceOptionFlags.None,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                }, new DataRectangle(buffer.DataPointer, data.GetLength(0) * 4));
            }
            textureView = new ShaderResourceView(device, tex);
        }

        public void RenderScene(DrawEventArgs args)
        {
            if (textureView == null) return;
            var ctx = Renderer.Device.ImmediateContext;

            ctx.VertexShader.Set(vertShader);
            ctx.PixelShader.Set(fragShader);
            ctx.InputAssembler.InputLayout = vertLayout;
            ctx.PixelShader.SetShaderResource(0, textureView);
            ctx.PixelShader.SetSampler(0, samplerStateMap);

            var verts = new[] {
                new Vert(){ pos = new Vector4(-1, -1, 0, 1), uv = new Vector2((float)HorizontalScale.Left, 1 - (float)(VerticalScale.Bottom / 127)) },
                new Vert(){ pos = new Vector4(-1, 1, 0, 1), uv = new Vector2((float)HorizontalScale.Left, 1 - (float)(VerticalScale.Top / 127)) },
                new Vert(){ pos = new Vector4(1, 1, 0, 1), uv = new Vector2((float)HorizontalScale.Right, 1 - (float)(VerticalScale.Top / 127)) },
                new Vert(){ pos = new Vector4(1, -1, 0, 1), uv = new Vector2((float)HorizontalScale.Right, 1 - (float)(VerticalScale.Bottom / 127)) },
            };

            var min = ScaledMin * this.max;
            var max = ScaledMax * this.max;

            for (int i = 0; i < 4; i++)
            {
                verts[i].max = (float)max;
                verts[i].min = (float)min;
                verts[i].pow = (float)ColorPow;
            }

            DataStream data;
            ctx.MapSubresource(vertexBuff, 0, MapMode.WriteDiscard, MapFlags.None, out data);
            data.Position = 0;
            data.WriteRange(new[] {
                verts[0], verts[1], verts[2],
                verts[0], verts[2], verts[3]
            });
            ctx.UnmapSubresource(vertexBuff, 0);
            ctx.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            ctx.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuff, 36, 0));
            ctx.Draw(6, 0);
        }

        void IDirect3D.Reset(DrawEventArgs args)
        {
            if (Renderer != null)
                Renderer.Reset(args);
        }

        void IDirect3D.Render(DrawEventArgs args)
        {
            if (Renderer != null)
                Renderer.Render(args);
        }
    }
}
