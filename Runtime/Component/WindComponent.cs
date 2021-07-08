using UnityEngine;

namespace Landscape.FoliagePipeline
{
  [ExecuteAlways]
  public class WindComponent : MonoBehaviour
  {
    [SerializeField]
    private FWindSettings _windSettings = FWindSettings.Calm;
    [SerializeField]
    private WindZone _sourceWindZone;
    [SerializeField]
    private Texture2D _gustNoise;
    public Texture2D perlinNoise;

    [HideInInspector]
    [SerializeField]
    private int _selectedPreset;
    private Quaternion _cachedRotation;
    private float _cachedWindMain;
    private float _cachedWindPulseFrequency;
    private float _cachedWindTurbulence;
    private double _smoothWindOffset;
    private double _cachedTime;

    public static WindComponent Instance { get; private set; }

    public FWindSettings Settings
    {
      get => this._windSettings;
      set
      {
        this._windSettings = value;
        this._windSettings.Apply();
        this.UpdateDirection(false);
      }
    }

    public WindZone Zone
    {
      get => this._sourceWindZone;
      set
      {
        this._sourceWindZone = value;
        if (!((Object) value != (Object) null))
          return;
        this.ValidateWindZone();
        this.CopyAndApply();
      }
    }

    public Texture2D GustNoise
    {
      get => this._gustNoise;
      set
      {
        this._gustNoise = value;
        this._windSettings.Apply(this._gustNoise);
      }
    }

    public void UpdateTime(double time)
    {
      double num = time - this._cachedTime;
      this._cachedTime = time;
      this._smoothWindOffset += num * (double) this.Settings.WindSpeed;
      Shader.SetGlobalVector("g_SmoothTime", new Vector4((float) this._smoothWindOffset * 6f, (float) this._smoothWindOffset * 0.15f, (float) this._smoothWindOffset * 3.5f, (float) this._smoothWindOffset * 3.5f));
    }

    void OnEnable()
    {
      WindComponent.Instance = this;
      this.ValidateWindZone();
      if ((Object)this._sourceWindZone != (Object)null)
        this.CopyFromWindZone();
      else
        //this.UpdateDirection(false);
        
      this._windSettings.Apply(this._gustNoise);

      Shader.SetGlobalTexture("g_PerlinNoise", perlinNoise);
    }

    void Update()
    {
      if ((Object) this._sourceWindZone != (Object) null && this.WindZoneHasChanged())
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

    private void CopyFromWindZone() => this.Settings = FWindSettings.FromWindZone(this._sourceWindZone);

    private bool WindZoneHasChanged() => this._cachedRotation != this._sourceWindZone.transform.rotation || (double) this._cachedWindMain != (double) this._sourceWindZone.windMain || ((double) this._cachedWindPulseFrequency != (double) this._sourceWindZone.windPulseFrequency || (double) this._cachedWindTurbulence != (double) this._sourceWindZone.windTurbulence);

    private void CacheWindZoneProperties()
    {
      this._cachedRotation = this._sourceWindZone.transform.rotation;
      this._cachedWindMain = this._sourceWindZone.windMain;
      this._cachedWindPulseFrequency = this._sourceWindZone.windPulseFrequency;
      this._cachedWindTurbulence = this._sourceWindZone.windTurbulence;
    }

    private void ValidateWindZone()
    {
      if (!((Object) this._sourceWindZone != (Object) null) || (uint) this._sourceWindZone.mode <= 0U)
        return;
      Debug.LogWarning((object) (this.GetType().Name + " requires a directional wind zone."), (Object) this);
    }

    private void UpdateDirection(bool useCache)
    {
      if ((Object) this._sourceWindZone != (Object) null || useCache && this.transform.rotation == this._cachedRotation)
        return;
      this._cachedRotation = this.transform.rotation;
      this._windSettings.WindDirection = FWindSettings.RotationToDirection(this.transform.rotation);
      this._windSettings.Apply();
    }
  }
}
