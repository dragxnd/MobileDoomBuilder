Shader "MobileUDB/VisualSurface"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _AlphaClip ("Alpha cutoff", Float) = 0.5
        _Sky ("Sky", Float) = 0
        _SkyTransfer ("Sky transfer", Vector) = (0,0,1,0)
        _SkySize ("Sky source size", Vector) = (256,128,0,0)
        _ZWrite ("Depth write", Float) = 1
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "VisualSurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull [_Cull]
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint; float _AlphaClip; float _Sky;
                float4 _SkyTransfer; float4 _SkySize;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float3 world : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output; output.world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.world); output.uv=input.uv; output.color=input.color; return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv=input.uv;
                if(_Sky>0.5)
                {
                    float3 direction=normalize(input.world-_WorldSpaceCameraPos);
                    uv=float2(atan2(direction.z,direction.x)*0.63661977, saturate(0.5+asin(direction.y)*0.63661977));
                    if(_SkyTransfer.x>0.5)
                    {
                        // MBF uses a fixed-point sidedef offset as a BAM angle, not pixels.
                        float column=floor(frac(atan2(direction.z,direction.x)*0.159154943+_SkyTransfer.y)*1024.0);
                        if(_SkyTransfer.z<0) column=1023.0-column;
                        float row=_SkyTransfer.w-asin(clamp(direction.y,-1.0,1.0))*81.4873309;
                        uv=float2((column+0.5)/_SkySize.x,1.0-row/_SkySize.y);
                    }
                }
                half4 sampled=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
                clip(sampled.a-_AlphaClip);
                return sampled*input.color*_Tint;
            }
            ENDHLSL
        }
    }
}
