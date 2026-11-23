using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Transformations : MonoBehaviour
{
    CreateGrid createGrid;

    Vector3[] startPos;

    [Header("Movement Controls")]
    public float moveFrequency = 2f;
    public float moveAmplitude = 5f;
    public float moveOffset = 0f;

    Vector3 startScale = Vector3.zero;

    [Header("Scale Controls")]
    public float scaleFrequency = 2f;
    public float scaleAmplitude = 5f;
    public float scaleOffset = 0f;

    [Header("Custom tweak: cos-based pulse")]
    // When on, each cube pulses between 0.4x and 1.6x of its start size with a phase that depends on
    // its grid index, so neighbours stay distinct and a gap remains for shadows. Off = original tutorial maths.
    public bool cosPulse = false;
    public float pulseAmount = 0.6f;
    public float pulsePhasePerCube = 0.07f;
    public float pulseScale = 1f;      // overall size multiplier of the pulse (1 = start scale x (1 +- pulseAmount))

    [Header("Custom tweak: distortion waves + click shockwave")]
    // Each wave starts at one cube and pushes every other cube away from that point by
    // sin(distance * waveNumber - age * waveSpeed * waveNumber), faded out over waveLife seconds.
    // Waves start at random cubes by themselves (autoWaveInterval) and on every left mouse click (shockwave).
    public bool waves = false;
    public float waveAmplitude = 0.9f;
    public float waveNumber = 1.4f;      // radians per unit of distance (smaller = longer wavelength)
    public float waveSpeed = 5f;         // units per second the front travels
    public float waveLife = 2.5f;        // seconds until the wave has faded to zero
    public float waveBand = 3f;          // thickness of the travelling band, in units
    public float autoWaveInterval = 1.2f; // seconds between random waves, 0 = clicks only
    public float clickWaveBoost = 1.6f;  // click shockwaves are this much stronger

    struct Wave { public Vector3 origin; public float start; public float amp; }
    readonly List<Wave> active = new List<Wave>();
    float nextAuto;

    [Header("Rotation Controls")]
    public float rotationSpeed = 1f;

    private void Start()
    {
        createGrid = GetComponent<CreateGrid>();
        startPos = createGrid.startPos;
        startScale = createGrid.grid[0].localScale;
    }

    // Cube nearest to a ray: the cubes shrink to zero scale, so a plain collider hit would often miss.
    Vector3 NearestCubeToRay(Ray ray)
    {
        float best = float.MaxValue; Vector3 pos = Vector3.zero;
        for (int i = 0; i < createGrid.grid.Length; i++)
        {
            if (!createGrid.grid[i].gameObject.activeSelf) continue;
            Vector3 p = createGrid.grid[i].position;
            float d = Vector3.Cross(ray.direction, p - ray.origin).sqrMagnitude;
            if (d < best) { best = d; pos = startPos[i]; }
        }
        return pos;
    }

    void UpdateWaves()
    {
        if (autoWaveInterval > 0f && Time.time >= nextAuto)
        {
            nextAuto = Time.time + autoWaveInterval;
            active.Add(new Wave { origin = startPos[Random.Range(0, startPos.Length)], start = Time.time, amp = waveAmplitude });
        }
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            active.Add(new Wave { origin = NearestCubeToRay(ray), start = Time.time, amp = waveAmplitude * clickWaveBoost });
        }
        active.RemoveAll(w => Time.time - w.start > waveLife);
    }

    Vector3 WaveOffset(Vector3 pos)
    {
        Vector3 sum = Vector3.zero;
        for (int k = 0; k < active.Count; k++)
        {
            Wave w = active[k];
            float age = Time.time - w.start;
            Vector3 dir = pos - w.origin;
            float d = dir.magnitude;
            dir = d > 0.001f ? dir / d : Vector3.up;
            float phase = (d - age * waveSpeed) * waveNumber;
            float front = (d - age * waveSpeed) / waveBand;
            float envelope = Mathf.Exp(-front * front) * (1f - age / waveLife);
            sum += dir * (w.amp * Mathf.Sin(phase) * envelope);
        }
        return sum;
    }

    void Update()
    {
        if (waves) UpdateWaves();
        for (int i = 0, z = 0; z < createGrid.gridResolution; z++)
        {
            for (int y = 0; y < createGrid.gridResolution; y++)
            {
                for (int x = 0; x < createGrid.gridResolution; x++, i++)
                {
                    createGrid.grid[i].localPosition = startPos[i] + Vector3.up * Mathf.Sin(Time.time * moveFrequency + moveOffset) * moveAmplitude
                        + (waves ? WaveOffset(startPos[i]) : Vector3.zero);

                    if (cosPulse)
                        createGrid.grid[i].localScale = startScale * pulseScale * (1f + pulseAmount * Mathf.Cos(Time.time * scaleFrequency + i * pulsePhasePerCube));
                    else
                        createGrid.grid[i].localScale = startScale * Mathf.Sin(Time.time * scaleFrequency) * scaleAmplitude + Vector3.one * scaleOffset;

                    createGrid.grid[i].Rotate(Vector3.forward * Time.deltaTime * rotationSpeed);
                }
            }
        }
    }
}
