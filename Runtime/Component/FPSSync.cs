using UnityEngine;
using System.Diagnostics;

public class FPSSync : MonoBehaviour
{
    public bool showFPS;
    public int maxFPS = 60;

    [Range(0, 2)]
    public int vsyncMode = 0;

    private int m_PrintState = 0;
    private long m_Millisecond = 0;
    public static Stopwatch stopwatch;


    void OnEnable()
    {
        stopwatch = new Stopwatch();
        Application.targetFrameRate = maxFPS;
        QualitySettings.vSyncCount = vsyncMode;
    }

    void OnGUI()
    {
        if (showFPS)
        {
            ++m_PrintState;
            if (m_PrintState % 5 == 0)
            {
                m_Millisecond = stopwatch.ElapsedMilliseconds;
            }

            GUIStyle FPSGUI = new GUIStyle();
            FPSGUI.fontSize = 32;
            FPSGUI.normal.textColor = new Color(1, 0, 0);
            GUI.Label(new Rect(32, 32, 512, 512), "ms:" + m_Millisecond, FPSGUI);
        }
    }
}
