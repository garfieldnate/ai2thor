// latest

Shader "Custom/BlendShader" {
     Properties
     {
         [MainTexture] _MainTex ("Base (RGB)", 2D) = "white" {}

         _MainTex1 ("Texture 1", 2D) = "white" {}
         _MainTex2 ("Texture 2", 2D) = "white" {}

         _BlendAmount ("Blend Amount", Range(0, 1)) = 0.5

         _LensDistortionStrength ("Lens Distortion Strength", Range (-20.0, 20.0))  = 1.0
        //  _LensDistortionTightness ("Lens Distortion Power", Range (-20.0, 20.0))  = 7.0
         _ZoomPercent ("Zoom Percent", Range (0.0, 5.0))  = 1.0


          _k1 ("K1 polynomial dist coeff", Range (-8.0, 8.0))  = -0.126
          _k2 ("K2 polynomial dist coeff", Range (-8.0, 8.0))  = 0.004
          _k3 ("K3 polynomial dist coeff", Range (-18.0, 18.0))  = 0.0
          _k4 ("K4 polynomial dist coeff", Range (-18.0, 18.0))  = 0.0

          _DistortionIntensityX ("Distort Strength X", Range (0.0, 6.0)) = 1.0
         _DistortionIntensityY ("Distort Strength Y", Range (0.0, 6.0)) = 1.0
          
         
         _OutOfBoundColour ("Outline Color", Color) = (0.0, 0.0, 0.0, 1.0)
     }
     SubShader
     {
         Pass
         {
             CGPROGRAM

              #pragma vertex vert
             #pragma fragment frag
             #include "UnityCG.cginc"

              uniform sampler2D _MainTex;
             uniform sampler2D _CameraDepthTexture;
             uniform half4 _MainTex_TexelSize;

             uniform float _LensDistortionStrength;
             uniform float4 _OutOfBoundColour;
            //  uniform float _LensDistortionTightness;

             uniform float _ZoomPercent;

            uniform float _DistortionIntensityX;
            uniform float _DistortionIntensityY;
             
             uniform float _k1;
             uniform float _k2;
             uniform float _k3;
             uniform float _k4;

            sampler2D _MainTex1;
            sampler2D _MainTex2;
            float _BlendAmount;

             
            //  uniform float4 _ScreenParams;
            //  uniform float4 _ProjectionParams;

              struct input
             {
                 float4 pos : POSITION;
                 half2 uv : TEXCOORD0;
             };

              struct output
             {
                 float4 pos : SV_POSITION;
                 half2 uv : TEXCOORD0;
             };


              output vert(input i)
             {
                 output o;
                 o.pos = UnityObjectToClipPos(i.pos);
                //  o.uv = i.uv;
                 o.uv = MultiplyUV(UNITY_MATRIX_TEXTURE0, i.uv);
                //  o.uv.y = 1 - o.uv.y;
                //  // why do we need this? cause sometimes the image I get is flipped. see: http://docs.unity3d.com/Manual/SL-PlatformDifferences.html
                //  #if UNITY_UV_STARTS_AT_TOP
                //  if (_MainTex_TexelSize.y < 0)
                //          o.uv.y = 1 - o.uv.y;
                //  #endif
                // if (_ProjectionParams.x >= 0)
                //     o.uv.y = 1 - o.uv.y;

                  return o;
             }

              fixed4 frag(output o) : COLOR
             {
                float effect = _LensDistortionStrength;
                float2 distortionStrengthXY = float2(_DistortionIntensityX, _DistortionIntensityY);
                float zoom_offset = (1.0 - _ZoomPercent) / 2.0;

                float2 centered_uv = o.uv - float2(0.5, 0.5);
                centered_uv = o.uv*2.0 -  float2(1.0, 1.0);
                //  centered_uv = o.uv -  float2(0.5, 0.5);

                zoom_offset = (1.0 - _ZoomPercent);
                centered_uv = (o.uv*2.0 * _ZoomPercent - float2(1.0, 1.0)) + float2(zoom_offset, zoom_offset);  
                
                
               
                float uv_dot = dot(centered_uv, centered_uv);
                float r = sqrt(uv_dot);
                // // For Atan based distortion
                float z = sqrt(1.0 - uv_dot * effect);
                // float z = sqrt(1.0 - r * r);
                float atan_distort = atan2(r, z) / UNITY_PI;

                float atan_distortX = atan2(r, sqrt(1.0 - uv_dot * _DistortionIntensityX)) / UNITY_PI;
                float atan_distortY = atan2(r, sqrt(1.0 - uv_dot * _DistortionIntensityY)) / UNITY_PI;
                float2 distort_uv = (centered_uv / r) * atan_distort;//+ float2(0.5, 0.5);

                distort_uv = (centered_uv / r) * float2(atan_distortX, atan_distortY);
               
                //return t;

                const float distortionMagnitude=abs(centered_uv.x*centered_uv.y);

                float smoothDistortionMagnitude = 1.0 + _k1 * pow(r, 2.0) + _k2 * pow(r, 4.0) + _k3 * pow(r,6.0) + _k4 * pow(r,8.0);

                float zoom_percent = 0.2;
                float translate = (1.0 - zoom_percent) / 2.0;
                float2 centered_uv_norm = normalize(centered_uv);

               
                
                // Works with zoom for poly distortion
                float2 uvDistorted = centered_uv * smoothDistortionMagnitude * distortionStrengthXY / 2.0 + float2(0.5, 0.5) ;

                // working for atan distortion
                // float2 uvDistorted = ( centered_uv / r ) * atan_distort + float2(0.5, 0.5);

                // float2 uvDistorted = distort_uv + float2(0.5, 0.5);

                // for xydistortion
                // float2 uvDistorted = distort_uv*2.0 + float2(0.5, 0.5);

            fixed4 col1 = tex2D(_MainTex1, o.uv);

            fixed4 col2 = tex2D(_MainTex2, uvDistorted);
            //Handle out of bound uv
            if (uvDistorted.x < 0 || uvDistorted.x > 1 || uvDistorted.y < 0 || uvDistorted.y > 1) {
                return _OutOfBoundColour;//uv out of bound so display out of bound color
            } else {
            }

             return lerp(col1, col2, _BlendAmount);

            // FOR atan radius cutoff
            // float radius = 1.0;
            //     if (uvDistorted.x > radius) {
            //         return _OutOfBoundColour;
            //     }
            //     else {
            //         return lerp(col1, col2, _BlendAmount);
            //     }

            
             }

              ENDCG
         }
     } 
 } 
 