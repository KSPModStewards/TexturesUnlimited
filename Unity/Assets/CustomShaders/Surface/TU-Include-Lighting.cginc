//custom lighting function to enable colored specular, SubSurf, and icon light multiply functionality
//three versions -- one for pbr/metallic, pbr/specular, and legacy/specular
#if TU_LIGHT_METAL		
	//replacement for Unity bridge method to call GI with custom structs
	inline void LightingTU_GI (SurfaceOutputTU s, UnityGIInput data, inout UnityGI gi)
	{
		UNITY_GI(gi, s, data);
	}

	//custom lighting function to enable SubSurf functionality
	inline half4 LightingTU(SurfaceOutputTU s, half3 viewDir, UnityGI gi)
	{
		s.Normal = normalize(s.Normal);
		
		//Unity 'Standard Metallic' lighting function, unabridged
		half oneMinusReflectivity;
		half3 specSampleColor;
		s.Albedo = DiffuseAndSpecularFromMetallic(s.Albedo, s.Metallic, /*out*/ specSampleColor, /*out*/ oneMinusReflectivity);
		half outputAlpha;
		s.Albedo = PreMultiplyAlpha (s.Albedo, s.Alpha, oneMinusReflectivity, /*out*/ outputAlpha);
		half4 c = UNITY_BRDF_PBS (s.Albedo, specSampleColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, gi.light, gi.indirect);
		c.rgb += UNITY_BRDF_GI (s.Albedo, specSampleColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, s.Occlusion, gi);
		c.a = outputAlpha;

		//subsurface scattering contribution
		#if TU_SUBSURF
			c.rgb += subsurf(_SubSurfScale, _SubSurfPower, _SubSurfDistort, _SubSurfAtten, s.Backlight.a, s.Albedo, s.Backlight.rgb, s.Normal, viewDir, gi.light.color, gi.light.dir);
		#endif
		#if TU_ICON
			//c.rgb *= _Multiplier.rrr;
		#endif
		
		return c;
	}


	inline half4 LightingTU_Deferred (SurfaceOutputTU s, float3 viewDir, UnityGI gi, out half4 outGBuffer0, out half4 outGBuffer1, out half4 outGBuffer2)
	{
		half oneMinusReflectivity;
		half3 specSampleColor;
		s.Albedo = DiffuseAndSpecularFromMetallic (s.Albedo, s.Metallic, /*out*/ specSampleColor, /*out*/ oneMinusReflectivity);

		UnityStandardData data;
		data.diffuseColor   = s.Albedo;
		data.occlusion      = s.Occlusion;
		data.specularColor  = specSampleColor;
		data.smoothness     = s.Smoothness;
		data.normalWorld    = s.Normal;

		UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

		half4 emission = half4(s.Emission, 1);

		// _LightColor0 and _WorldSpaceLightPos0 are legacy properties of the main light available in forward, and correctly set in deferred rendering by the Deferred mod
		// This means subsurface scattering will only work for the main light for now. In the future move subsurface scattering to a second forward-only material that gets added
		// to the GameObject so that all lights will work with it
		#if TU_SUBSURF
			emission.rgb += subsurf(_SubSurfScale, _SubSurfPower, _SubSurfDistort, _SubSurfAtten, s.Backlight.a, s.Albedo, s.Backlight.rgb, s.Normal, viewDir, _LightColor0, _WorldSpaceLightPos0.rgb);
		#endif

		return emission;
	}


#endif

#if TU_LIGHT_SPEC
	//replacement for Unity bridge method to call GI with custom structs
	inline void LightingTU_GI (SurfaceOutputTU s, UnityGIInput data, inout UnityGI gi)
	{
		UNITY_GI(gi, s, data);
	}	

	//custom lighting function to enable SubSurf functionality
	inline half4 LightingTU(SurfaceOutputTU s, half3 viewDir, UnityGI gi)
	{
		s.Normal = normalize(s.Normal);
		
		//Unity 'Standard' lighting function, unabridged
		// energy conservation
		half oneMinusReflectivity;
		s.Albedo = EnergyConservationBetweenDiffuseAndSpecular (s.Albedo, s.SpecularColor, /*out*/ oneMinusReflectivity);
		// shader relies on pre-multiply alpha-blend (_SrcBlend = One, _DstBlend = OneMinusSrcAlpha)
		// this is necessary to handle transparency in physically correct way - only diffuse component gets affected by alpha
		half outputAlpha;
		s.Albedo = PreMultiplyAlpha (s.Albedo, s.Alpha, oneMinusReflectivity, /*out*/ outputAlpha);
		half4 c = UNITY_BRDF_PBS (s.Albedo, s.SpecularColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, gi.light, gi.indirect);
		c.rgb += UNITY_BRDF_GI (s.Albedo, s.SpecularColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, s.Occlusion, gi);
		c.a = outputAlpha;
		
		#if TU_SUBSURF
			c.rgb += subsurf(_SubSurfScale, _SubSurfPower, _SubSurfDistort, _SubSurfAtten, s.Backlight.a, s.Albedo, s.Backlight.rgb, s.Normal, viewDir, gi.light.color, gi.light.dir);
		#endif
		#if TU_ICON
			//c.rgb *= _Multiplier.rrr;
		#endif
		
		return c;
	}


	inline half4 LightingTU_Deferred (SurfaceOutputTU s, float3 viewDir, UnityGI gi, out half4 outGBuffer0, out half4 outGBuffer1, out half4 outGBuffer2)
	{
		// energy conservation
		half oneMinusReflectivity;
		s.Albedo = EnergyConservationBetweenDiffuseAndSpecular (s.Albedo, s.SpecularColor, /*out*/ oneMinusReflectivity);

		UnityStandardData data;
		data.diffuseColor   = s.Albedo;
		data.occlusion      = s.Occlusion;
		data.specularColor  = s.SpecularColor;
		data.smoothness     = s.Smoothness;
		data.normalWorld    = s.Normal;

		UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

		half4 emission = half4(s.Emission, 1);
	
		// _LightColor0 and _WorldSpaceLightPos0 are legacy properties of the main light available in forward, and correctly set in deferred rendering by the Deferred mod
		// This means subsurface scattering will only work for the main light for now. In the future move subsurface scattering to a second forward-only material that gets added
		// to the GameObject so that all lights will work with it
		#if TU_SUBSURF
			emission.rgb += subsurf(_SubSurfScale, _SubSurfPower, _SubSurfDistort, _SubSurfAtten, s.Backlight.a, s.Albedo, s.Backlight.rgb, s.Normal, viewDir, _LightColor0, _WorldSpaceLightPos0.rgb);
		#endif

		return emission;
	}


#endif

#if TU_LIGHT_SPECLEGACY	
	//replacement for Unity bridge method to call GI with custom structs
	inline void LightingTU_GI (
		SurfaceOutputTU s,
		UnityGIInput data,
		inout UnityGI gi)
	{
		gi = UnityGlobalIllumination (data, 1.0, s.Normal);
	}

	inline half4 LightingTU(SurfaceOutputTU s, half3 viewDir, UnityGI gi)
	{
		UnityLight light = gi.light;

		#if TU_BUMPMAP
			s.Normal = normalize(s.Normal);
		#endif

		s.Smoothness = max(0.01, s.Smoothness);
		//standard blinn-phong lighting model
		//diffuse light intensity, from surface normal and light direction
		half diff = max (0, dot (s.Normal, light.dir));
		//specular light calculations
		half3 h = normalize (light.dir + viewDir);
		float nh = max (0, dot (s.Normal, h));
		float spec = pow (nh, s.Smoothness * 128);
		half3 specCol = spec * s.SpecularColor;
		
		//output fragment color; Unity adds Emission to it through some other method
		half4 c;
		#if TU_ICON
			//diff *= _Multiplier;
		#endif
		c.rgb = ((s.Albedo * _LightColor0.rgb * diff + _LightColor0.rgb * specCol));
		c.rgb += s.Albedo * gi.indirect.diffuse;
		c.a = s.Alpha;
		
		#if TU_SUBSURF
			c.rgb += subsurf(_SubSurfScale, _SubSurfPower, _SubSurfDistort, _SubSurfAtten, s.Backlight.a, s.Albedo, s.Backlight.rgb, s.Normal, viewDir, _LightColor0.rgb, light.dir);
		#endif
		#if TU_ICON
			//c.rgb *= _Multiplier.rrr;
		#endif
		
		return c;
	}

	inline half4 LightingTU_Deferred (SurfaceOutputTU s, half3 viewDir, UnityGI gi, out half4 outGBuffer0, out half4 outGBuffer1, out half4 outGBuffer2)
	{
		#if TU_BUMPMAP
			s.Normal = normalize(s.Normal);
		#endif
		
		s.Smoothness = max(0.01, s.Smoothness);

		UnityStandardData data;
		data.diffuseColor   = s.Albedo;
		data.occlusion      = 1;
		// PI factor come from StandardBDRF (UnityStandardBRDF.cginc:351 for explanation)
		data.specularColor  = _SpecColor.rgb * (1/UNITY_PI);
		data.smoothness     = s.SpecularColor;
		data.normalWorld    = s.Normal;

		UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

		half4 emission = half4(s.Emission, 1);

		#ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
			emission.rgb += s.Albedo * gi.indirect.diffuse;
		#endif

		#if TU_SUBSURF
			emission.rgb += subsurf(_SubSurfScale, _SubSurfPower, _SubSurfDistort, _SubSurfAtten, s.Backlight.a, s.Albedo, s.Backlight.rgb, s.Normal, viewDir, gi.light.color, gi.light.dir);
		#endif

		return emission;
	}

#endif
