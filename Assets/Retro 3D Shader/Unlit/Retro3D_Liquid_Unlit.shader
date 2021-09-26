// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Shader created with Shader Forge v1.40 
// Shader Forge (c) Freya Holmer - http://www.acegikmo.com/shaderforge/
// Note: Manually altering this data may prevent you from opening it in Shader Forge
/*SF_DATA;ver:1.40;sub:START;pass:START;ps:flbk:,iptp:0,cusa:False,bamd:0,cgin:,cpap:True,lico:1,lgpr:1,limd:0,spmd:1,trmd:0,grmd:0,uamb:True,mssp:True,bkdf:False,hqlp:False,rprd:False,enco:False,rmgx:True,imps:True,rpth:0,vtps:0,hqsc:True,nrmq:1,nrsp:0,vomd:0,spxs:False,tesm:0,olmd:1,culm:2,bsrc:0,bdst:1,dpts:2,wrdp:True,dith:1,atcv:False,rfrpo:True,rfrpn:Refraction,coma:15,ufog:True,aust:True,igpj:False,qofs:0,qpre:2,rntp:3,fgom:False,fgoc:False,fgod:False,fgor:False,fgmd:0,fgcr:0.5,fgcg:0.5,fgcb:0.5,fgca:1,fgde:0.01,fgrn:0,fgrf:300,stcl:False,atwp:False,stva:128,stmr:255,stmw:255,stcp:6,stps:0,stfa:0,stfz:0,ofsf:0,ofsu:0,f2p0:False,fnsp:False,fnfb:False,fsmp:False;n:type:ShaderForge.SFN_Final,id:4013,x:32802,y:32704,varname:node_4013,prsc:2|diff-1980-OUT,normal-4210-OUT,emission-6344-OUT,clip-4687-OUT,voffset-9581-XYZ;n:type:ShaderForge.SFN_Color,id:1304,x:32174,y:32775,ptovrint:False,ptlb:Color,ptin:_Color,varname:node_1304,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,c1:1,c2:1,c3:1,c4:1;n:type:ShaderForge.SFN_FragmentPosition,id:5440,x:31426,y:32983,cmnt:Vertex snapping - Screen Space,varname:node_5440,prsc:2;n:type:ShaderForge.SFN_Transform,id:3472,x:31635,y:32983,varname:node_3472,prsc:2,tffrom:0,tfto:3|IN-5440-XYZ;n:type:ShaderForge.SFN_Multiply,id:1173,x:31816,y:33072,varname:node_1173,prsc:2|A-3472-XYZ,B-583-OUT;n:type:ShaderForge.SFN_Round,id:5805,x:31981,y:33072,varname:node_5805,prsc:2|IN-1173-OUT;n:type:ShaderForge.SFN_Subtract,id:6209,x:32165,y:33072,varname:node_6209,prsc:2|A-5805-OUT,B-1173-OUT;n:type:ShaderForge.SFN_Divide,id:414,x:32335,y:33072,varname:node_414,prsc:2|A-6209-OUT,B-583-OUT;n:type:ShaderForge.SFN_Transform,id:9581,x:32496,y:33072,varname:node_9581,prsc:2,tffrom:3,tfto:0|IN-414-OUT;n:type:ShaderForge.SFN_Tex2d,id:109,x:32174,y:32583,ptovrint:False,ptlb:Diffuse,ptin:_Diffuse,varname:node_109,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,ntxv:0,isnm:False|UVIN-510-OUT;n:type:ShaderForge.SFN_Multiply,id:1980,x:32383,y:32700,varname:node_1980,prsc:2|A-109-RGB,B-1304-RGB;n:type:ShaderForge.SFN_Rotator,id:6623,x:33066,y:32259,varname:node_6623,prsc:2|UVIN-8455-UVOUT,ANG-4959-OUT;n:type:ShaderForge.SFN_Slider,id:7293,x:31992,y:32334,ptovrint:False,ptlb:Swirl Amount,ptin:_SwirlAmount,varname:node_7293,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:-4,cur:-2,max:4;n:type:ShaderForge.SFN_TexCoord,id:8455,x:31544,y:32586,varname:node_8455,prsc:2,uv:0,uaff:False;n:type:ShaderForge.SFN_Subtract,id:4229,x:31793,y:32435,varname:node_4229,prsc:2|A-8455-UVOUT,B-1271-OUT;n:type:ShaderForge.SFN_Vector1,id:1271,x:31793,y:32368,varname:node_1271,prsc:2,v1:0.5;n:type:ShaderForge.SFN_Divide,id:2166,x:31987,y:32435,varname:node_2166,prsc:2|A-4229-OUT,B-1271-OUT;n:type:ShaderForge.SFN_Abs,id:5350,x:32174,y:32435,varname:node_5350,prsc:2|IN-2166-OUT;n:type:ShaderForge.SFN_Length,id:4400,x:32353,y:32435,varname:node_4400,prsc:2|IN-5350-OUT;n:type:ShaderForge.SFN_OneMinus,id:5186,x:32520,y:32435,varname:node_5186,prsc:2|IN-4400-OUT;n:type:ShaderForge.SFN_Clamp01,id:866,x:32691,y:32435,varname:node_866,prsc:2|IN-5186-OUT;n:type:ShaderForge.SFN_Multiply,id:2111,x:32691,y:32294,varname:node_2111,prsc:2|A-866-OUT,B-7293-OUT,C-1239-OUT;n:type:ShaderForge.SFN_Time,id:5790,x:31800,y:32045,varname:node_5790,prsc:2;n:type:ShaderForge.SFN_Multiply,id:2289,x:32546,y:32094,varname:node_2289,prsc:2|A-5790-T,B-9996-OUT;n:type:ShaderForge.SFN_Slider,id:3074,x:31992,y:32233,ptovrint:False,ptlb:Outer Swirl Speed,ptin:_OuterSwirlSpeed,varname:_Swirl_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:-1,cur:0,max:1;n:type:ShaderForge.SFN_Frac,id:6678,x:32714,y:32094,varname:node_6678,prsc:2|IN-2289-OUT;n:type:ShaderForge.SFN_Multiply,id:1955,x:32875,y:32094,varname:node_1955,prsc:2|A-6678-OUT,B-8910-OUT;n:type:ShaderForge.SFN_Tau,id:8910,x:32747,y:31983,varname:node_8910,prsc:2;n:type:ShaderForge.SFN_Add,id:4959,x:32875,y:32294,varname:node_4959,prsc:2|A-2111-OUT,B-1955-OUT;n:type:ShaderForge.SFN_Multiply,id:8963,x:31992,y:32045,varname:node_8963,prsc:2|A-6964-OUT,B-5790-T;n:type:ShaderForge.SFN_Sin,id:4218,x:32164,y:32045,varname:node_4218,prsc:2|IN-8963-OUT;n:type:ShaderForge.SFN_RemapRange,id:1239,x:32341,y:32045,varname:node_1239,prsc:2,frmn:-1,frmx:1,tomn:0,tomx:1|IN-4218-OUT;n:type:ShaderForge.SFN_Slider,id:6964,x:31643,y:31961,ptovrint:False,ptlb:Inner Swirl Speed,ptin:_InnerSwirlSpeed,cmnt:Inner Swirl Sine Wave,varname:_OuterSwirlSpeed_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:-1,cur:0,max:1;n:type:ShaderForge.SFN_RemapRange,id:9996,x:32341,y:32202,varname:node_9996,prsc:2,frmn:-0.1,frmx:0.1,tomn:-0.01,tomx:0.01|IN-3074-OUT;n:type:ShaderForge.SFN_Slider,id:1323,x:31478,y:32811,ptovrint:False,ptlb:Alpha Clip,ptin:_AlphaClip,varname:node_1323,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0.25,max:1;n:type:ShaderForge.SFN_Power,id:4687,x:32008,y:32785,varname:node_4687,prsc:2|VAL-109-A,EXP-6829-OUT;n:type:ShaderForge.SFN_RemapRange,id:6829,x:31817,y:32785,varname:node_6829,prsc:2,frmn:0,frmx:1,tomn:0,tomx:4|IN-1323-OUT;n:type:ShaderForge.SFN_ValueProperty,id:9,x:32087,y:33568,ptovrint:False,ptlb:Normal Intensity,ptin:_NormalIntensity,varname:node_7251,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:1;n:type:ShaderForge.SFN_Vector3,id:8817,x:32268,y:33474,varname:node_8817,prsc:2,v1:0,v2:0,v3:1;n:type:ShaderForge.SFN_OneMinus,id:8053,x:32268,y:33568,varname:node_8053,prsc:2|IN-9-OUT;n:type:ShaderForge.SFN_Lerp,id:4210,x:32494,y:33300,varname:node_4210,prsc:2|A-6215-RGB,B-8817-OUT,T-8053-OUT;n:type:ShaderForge.SFN_Tex2d,id:6215,x:32268,y:33302,ptovrint:True,ptlb:Normal,ptin:_BumpMap,varname:_BumpMap,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,ntxv:3,isnm:True;n:type:ShaderForge.SFN_ValueProperty,id:583,x:31635,y:33175,ptovrint:False,ptlb:Vertex Detail,ptin:_VertexDetail,varname:node_583,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:80;n:type:ShaderForge.SFN_Posterize,id:6344,x:32577,y:32700,varname:node_6344,prsc:2|IN-1980-OUT,STPS-1528-OUT;n:type:ShaderForge.SFN_ValueProperty,id:1528,x:32383,y:32875,ptovrint:False,ptlb:Colour Depth,ptin:_ColourDepth,varname:node_1528,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:32;n:type:ShaderForge.SFN_ValueProperty,id:2939,x:31149,y:32467,ptovrint:False,ptlb:Y Scroll,ptin:_YScroll,varname:_XScroll_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:0;n:type:ShaderForge.SFN_ValueProperty,id:3784,x:31149,y:32367,ptovrint:False,ptlb:X Scroll,ptin:_XScroll,varname:node_344,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:0;n:type:ShaderForge.SFN_Append,id:5569,x:31358,y:32395,varname:node_5569,prsc:2|A-3784-OUT,B-2939-OUT;n:type:ShaderForge.SFN_Multiply,id:7752,x:31347,y:32157,varname:node_7752,prsc:2|A-5569-OUT,B-5790-T;n:type:ShaderForge.SFN_Add,id:9446,x:31347,y:31950,varname:node_9446,prsc:2|A-8455-UVOUT,B-7752-OUT;n:type:ShaderForge.SFN_Add,id:510,x:31924,y:32583,varname:node_510,prsc:2|A-6623-UVOUT,B-9446-OUT;proporder:109-1304-7293-6964-3074-583-1323-6215-9-1528-2939-3784;pass:END;sub:END;*/

Shader "Retro3D/Unlit/Retro3D_Liquid_Unlit" {
    Properties {
        _Diffuse ("Diffuse", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SwirlAmount ("Swirl Amount", Range(-4, 4)) = -2
        _InnerSwirlSpeed ("Inner Swirl Speed", Range(-1, 1)) = 0
        _OuterSwirlSpeed ("Outer Swirl Speed", Range(-1, 1)) = 0
        _VertexDetail ("Vertex Detail", Float ) = 80
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.25
        _BumpMap ("Normal", 2D) = "bump" {}
        _NormalIntensity ("Normal Intensity", Float ) = 1
        _ColourDepth ("Colour Depth", Float ) = 32
        _YScroll ("Y Scroll", Float ) = 0
        _XScroll ("X Scroll", Float ) = 0
        [HideInInspector]_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader {
        Tags {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="ForwardBase"
            }
            Cull Off
            
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #pragma multi_compile_fwdbase_fullshadows
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _NormalIntensity)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                float3 tangentDir : TEXCOORD3;
                float3 bitangentDir : TEXCOORD4;
                float4 projPos : TEXCOORD5;
                UNITY_FOG_COORDS(6)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                UNITY_TRANSFER_FOG(o,o.pos);
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                i.normalDir = normalize(i.normalDir);
                i.normalDir *= faceSign;
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float _NormalIntensity_var = UNITY_ACCESS_INSTANCED_PROP( Props, _NormalIntensity );
                float3 normalLocal = lerp(_BumpMap_var.rgb,float3(0,0,1),(1.0 - _NormalIntensity_var));
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_510 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_510, _Diffuse));
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                clip( BinaryDither2x2(pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)) - 1.5, sceneUVs) );
////// Lighting:
////// Emissive:
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float3 node_1980 = (_Diffuse_var.rgb*_Color_var.rgb);
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 emissive = floor(node_1980 * _ColourDepth_var) / (_ColourDepth_var - 1);
                float3 finalColor = emissive;
                fixed4 finalRGBA = fixed4(finalColor,1);
                UNITY_APPLY_FOG(i.fogCoord, finalRGBA);
                return finalRGBA;
            }
            ENDCG
        }
        Pass {
            Name "ShadowCaster"
            Tags {
                "LightMode"="ShadowCaster"
            }
            Offset 1, 1
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float2 texcoord0 : TEXCOORD0;
            };
            struct VertexOutput {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD1;
                float4 posWorld : TEXCOORD2;
                float4 projPos : TEXCOORD3;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                TRANSFER_SHADOW_CASTER(o)
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_510 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_510, _Diffuse));
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                clip( BinaryDither2x2(pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)) - 1.5, sceneUVs) );
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
///    CustomEditor "ShaderForgeMaterialInspector"
}
