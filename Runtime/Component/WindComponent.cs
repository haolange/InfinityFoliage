using UnityEngine;

namespace Landscape.FoliagePipeline
{
  [ExecuteAlways]
  public class WindComponent : MonoBehaviour
  {
    [SerializeField]
    private FWindSettings windSettings = FWindSettings.Calm;
    [SerializeField]
    private WindZone m_sourceWindZone;
    [SerializeField]
    private Texture2D m_gustNoise;
    public Texture2D m_perlinNoise;

    [HideInInspector]
    [SerializeField]
    private int m_selectedPreset;
    private Quaternion m_cachedRotation;
    private float m_cachedWindMain;
    private float m_cachedWindPulseFrequency;
    private float m_cachedWindTurbulence;
    private double m_smoothWindOffset;
    private double m_cachedTime;

    public static WindComponent Instance { get; private set; }

    public FWindSettings Settings
    {
      get => windSettings;
      set
      {
        windSettings = value;
        windSettings.Apply();
        UpdateDirection(false);
      }
    }

    public WindZone Zone
    {
      get => m_sourceWindZone;
      set
      {
        m_sourceWindZone = value;
        if (!((Object) value != (Object) null))
          return;
        ValidateWindZone();
        CopyAndApply();
      }
    }

    public Texture2D GustNoise
    {
      get => m_gustNoise;
      set
      {
        m_gustNoise = value;
        windSettings.Apply(m_gustNoise);
      }
    }

    public void UpdateTime(double time)
    {
      double num = time - m_cachedTime;
      m_cachedTime = time;
      m_smoothWindOffset += num * (double)Settings.WindSpeed;
      Shader.SetGlobalVector("g_SmoothTime", new Vector4((float)m_smoothWindOffset * 6f, (float)m_smoothWindOffset * 0.15f, (float)m_smoothWindOffset * 3.5f, (float)m_smoothWindOffset * 3.5f));
    }

    void OnEnable()
    {
      WindComponent.Instance = this;
      this.ValidateWindZone();
      if ((Object)m_sourceWindZone != (Object)null)
        this.CopyFromWindZone();
      else
        //this.UpdateDirection(false);
        
      windSettings.Apply(m_gustNoise);

      Shader.SetGlobalTexture("g_PerlinNoise", m_perlinNoise);
    }

    void Update()
    {
      if ((Object) m_sourceWindZone != (Object) null && this.WindZoneHasChanged())
      {
        this.CopyAndApply();
      }
        
      if (Application.isPlaying)
      {
        this.UpdateTime((double) Time.time);
      }

      this.UpdateDirection(true);
    }

    private void CopyAndApply()
    {
      this.CacheWindZoneProperties();
      this.CopyFromWindZone();
    }

    private void CopyFromWindZone() => this.Settings = FWindSettings.FromWindZone(m_sourceWindZone);

    private bool WindZoneHasChanged() => m_cachedRotation != m_sourceWindZone.transform.rotation || (double)m_cachedWindMain != (double) m_sourceWindZone.windMain || ((double)m_cachedWindPulseFrequency != (double) this.m_sourceWindZone.windPulseFrequency || (double)m_cachedWindTurbulence != (double) this.m_sourceWindZone.windTurbulence);

    private void CacheWindZoneProperties()
    {
      m_cachedRotation = m_sourceWindZone.transform.rotation;
      m_cachedWindMain = m_sourceWindZone.windMain;
      m_cachedWindPulseFrequency = m_sourceWindZone.windPulseFrequency;
      m_cachedWindTurbulence = m_sourceWindZone.windTurbulence;
    }

    private void ValidateWindZone()
    {
      if (!((Object)m_sourceWindZone != (Object) null) || (uint)m_sourceWindZone.mode <= 0U)
        return;
      Debug.LogWarning((object) (this.GetType().Name + " requires a directional wind zone."), (Object) this);
    }

    private void UpdateDirection(bool useCache)
    {
      if ((Object)m_sourceWindZone != (Object) null || useCache && transform.rotation == m_cachedRotation)
        return;
      m_cachedRotation = transform.rotation;
      windSettings.WindDirection = FWindSettings.RotationToDirection(transform.rotation);
      windSettings.Apply();
    }
  }
}
