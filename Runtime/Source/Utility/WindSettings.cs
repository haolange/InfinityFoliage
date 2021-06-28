using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Landscape.FoliagePipeline
{
  [Serializable]
  public struct FWindSettings
  {
    [FormerlySerializedAs("GustDirection")]
    public Vector2 WindDirection;
    [FormerlySerializedAs("GustStrength")]
    [Range(0.0f, 1f)]
    public float WindStrength;
    [FormerlySerializedAs("GustSpeed")]
    [Range(0.5f, 1f)]
    public float WindSpeed;
    [FormerlySerializedAs("ShiverStrength")]
    [Range(0.0f, 1f)]
    public float Turbulence;

    public static FWindSettings Calm => new FWindSettings()
    {
      WindDirection = new Vector2(0.7f, 0.3f),
      WindStrength = 0.05f,
      Turbulence = 0.05f,
      WindSpeed = 0.5f
    };

    public static FWindSettings Breeze => new FWindSettings()
    {
      WindDirection = new Vector2(0.7f, 0.3f),
      WindStrength = 0.2f,
      Turbulence = 0.2f,
      WindSpeed = 0.5f
    };

    public static FWindSettings StrongBreeze => new FWindSettings()
    {
      WindDirection = new Vector2(0.7f, 0.3f),
      WindStrength = 0.5f,
      Turbulence = 0.5f,
      WindSpeed = 0.75f
    };

    public static FWindSettings Storm => new FWindSettings()
    {
      WindDirection = new Vector2(0.7f, 0.3f),
      WindStrength = 1f,
      Turbulence = 1f,
      WindSpeed = 1f
    };

    public static FWindSettings FromWindZone(WindZone windZone) => new FWindSettings()
    {
      WindStrength = windZone.windMain * 0.2f,
      WindSpeed = windZone.windPulseFrequency,
      Turbulence = windZone.windTurbulence * 0.2f,
      WindDirection = FWindSettings.RotationToDirection(windZone.transform.rotation)
    };

    public static Vector2 RotationToDirection(Quaternion quaternion)
    {
      float y = quaternion.eulerAngles.y;
      return new Vector2(Mathf.Sin(y * ((float) Math.PI / 180f)), Mathf.Cos(y * ((float) Math.PI / 180f))).normalized;
    }

    public FWindSettings(in FWindSettings other)
    {
      this.WindDirection = other.WindDirection;
      this.WindStrength = other.WindStrength;
      this.WindSpeed = other.WindSpeed;
      this.Turbulence = other.Turbulence;
    }

    public FWindSettings(
      Vector2 gustDirection,
      float windStrength,
      float windSpeed,
      float turbulence)
    {
      this.WindDirection = gustDirection;
      this.WindStrength = windStrength;
      this.WindSpeed = windSpeed;
      this.Turbulence = turbulence;
    }

    public void Apply(Texture2D gustNoise)
    {
      Shader.SetGlobalTexture("g_GustNoise", (Texture) gustNoise);
      this.Apply();
    }

    public void Apply()
    {
      Shader.SetGlobalVector("g_WindDirection", new Vector4(this.WindDirection.x, 0.0f, this.WindDirection.y));
      Shader.SetGlobalVector("g_Wind", new Vector4(this.WindSpeed, this.WindStrength));
      Shader.SetGlobalVector("g_Turbulence", new Vector4(this.WindSpeed, this.Turbulence));
    }

    public void ApplyToWindZone(WindZone zone)
    {
      zone.windMain = this.WindStrength * 5f;
      zone.windPulseFrequency = this.WindSpeed;
      zone.windTurbulence = this.Turbulence * 5f;
    }
  }
}
