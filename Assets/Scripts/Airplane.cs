using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Diagnostics; // For starting Python scripts
using System.Runtime.InteropServices; // For DllImport
using TMPro;


public class Airplane : MonoBehaviour {

    [Header("Level Information")]
    LevelData.Level.Event[] noteEventList;
    private int curMode = -1;
    private int curEvent = 0;
    private float totalBeat = 0;
    private float baseTime = 0f;

    [Header("Starting Information")]
    public TextMeshProUGUI instructionText;
    public float pitchTolerance = 300f;
    private Vector3 introStartPosition;
    private Vector3 introEndPosition;
    private bool isWaitingForPitch = false;
    private bool isPitchCorrect = false;
    private bool isAnimating = true;
    private float elapsedTime = 0;
    public float duration = 5f;

    [Header("Airplane Settings")]
    public static float targetPitch = 100f;
    public float pitchSensitivity = 3f; // How sensitive the airplane is to pitch changes
    public float gravity = 9.8f;          // Gravitational acceleration (m/s^2)
    private float verticalVelocity = 0f;
    private float lerpSpeed = 50f;
    private Rigidbody rb;
    public GameObject explosionEffect;


    [Header("Propeller Settings")]
    public Transform propeller;       // Reference to the propeller Transform
    public float maxSpinSpeed = 10000f; // Maximum spin speed for the propeller
    public static bool trillState = false;  // Lip trill intensity (0 to 1)

    [Header("Audio Processing")]
    private Process pythonProcess; // Process to handle the Python script
    private UDPReceiver trillReceiver; // Keeps the trillReceiver logic
    private float[] audioData;

    [Header("Audio Settings")]
    public uint sampleRate = 44100;  // Audio sample rate
    public uint bufferSize = 2048;   // Buffer size for pitch detection
    private AudioClip microphoneClip;
    private bool isMicrophoneActive = false;


    void Awake() {
        baseTime = GlobalSettings.curBeat();
    }

    void Start() {
        instructionText.gameObject.SetActive(false);
        curMode = 0;
        rb = GetComponent<Rigidbody>();
        trillReceiver = new UDPReceiver(5007, ReceiveTrillData);
        AubioWrapper.Initialize(bufferSize, bufferSize / 2, sampleRate);
        StartMicrophone();
        // Start game stuff 
        introStartPosition = new Vector3(
            GlobalSettings.levelData.intro.path[0].x,
            0,
            GlobalSettings.levelData.intro.path[0].z
        );
        introEndPosition = new Vector3(
            GlobalSettings.levelData.intro.path[1].x,
            GlobalSettings.key2height(GlobalSettings.levelData.intro.startNote),
            GlobalSettings.levelData.intro.path[1].z
        );
        UnityEngine.Debug.Log($"position: {introStartPosition.x}, {introStartPosition.z}");
        transform.position = introStartPosition;
        isAnimating = true; // Start animation
    } 

    
    void AnimateAirplane() {
        if (elapsedTime < duration) {
            elapsedTime += Time.deltaTime;
            transform.position = Vector3.Lerp(introStartPosition, introEndPosition, elapsedTime / duration);
            Vector3 direction = (introEndPosition - transform.position).normalized;
            if (direction != Vector3.zero) {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
            }
        }
        else {
            isAnimating = false;

            instructionText.text = "Sing the right note to make the airplane start";
            instructionText.gameObject.SetActive(true);
            isWaitingForPitch = true;

            GlobalSettings.midiNotePlayer.StartNote(GlobalSettings.levelData.intro.startNote);
        }
    }
    void WaitForCorrectPitch() {
        float startPitch = GlobalSettings.levelData.intro.startNote;

        DetectPitch();
        if (trillState && Mathf.Abs(startPitch - targetPitch) <= pitchTolerance) {
            isPitchCorrect = true;
            instructionText.gameObject.SetActive(false);
            GlobalSettings.midiNotePlayer.StopNote();

            // Transition to main game state
            GlobalSettings.gameState = 1; // Game starts
            GlobalSettings.level = 0;
            Time.timeScale = 0;
            UnityEngine.Debug.Log("Correct pitch detected! Loading scenes...");
        }
    }

    void StartMicrophone() {
        if (Microphone.devices.Length > 0) {
            string micName = Microphone.devices[0];
            microphoneClip = Microphone.Start(micName, true, 1, (int)sampleRate);
            isMicrophoneActive = true;
            UnityEngine.Debug.Log($"Microphone started: {micName}");
        }
        else UnityEngine.Debug.LogError("No microphone detected!");
    }

    void Update() {
        UnityEngine.Debug.Log("Timescale: " + Time.timeScale);
        if (Time.timeScale <= 1e-6) return;
        if (GlobalSettings.gameState == 0) {
            // still in starting mode
            if (isAnimating) { AnimateAirplane(); return; }
            if (isWaitingForPitch && !isPitchCorrect) {WaitForCorrectPitch(); return;}

            // if bla bla bla
            GlobalSettings.gameState = 1;
            // prevent race condition
            return;
        }
        // Use aubio_get_pitch to detect pitch and update targetPitch
        if (GlobalSettings.level == -1) return;
        float currentBeat = GlobalSettings.curBeat();
        DetectPitch();
        if (curMode != GlobalSettings.level) {
            curMode = GlobalSettings.level;
            baseTime = GlobalSettings.curBeat();
            curEvent = 0;
            totalBeat = 0;
            noteEventList = GlobalSettings.levelData.levels[GlobalSettings.level].level;
        }

        if (trillState && GlobalSettings.gameState != -1) {
            rb.useGravity = false;
            float height = GlobalSettings.key2height(targetPitch);
            float verticalInput = Mathf.Lerp(
                transform.position.y,
                height,
                Time.deltaTime * lerpSpeed // Increase interpolation speed
            );
            //UnityEngine.Debug.Log($"Target pitch: {targetPitch - GlobalSettings.heightOffset}, position: {transform.position.y}, vertical: {verticalInput}");

            transform.position = GlobalSettings.slideControl.getPosition(currentBeat - baseTime);
            transform.position = new Vector3(transform.position.x, height, transform.position.z);
        } else {
            rb.isKinematic = false;
            rb.useGravity = true;
            Vector3 targetPosition = GlobalSettings.slideControl.getPosition(currentBeat - baseTime);
            rb.position = new Vector3(targetPosition.x, rb.position.y, targetPosition.z);
            rb.rotation = Quaternion.LookRotation(GlobalSettings.slideControl.getForward(currentBeat - baseTime));
        }
        // Handle propeller spinning
        float spinSpeed = (trillState ? 1.0f : 0.0f) * maxSpinSpeed;
        propeller.Rotate(Vector3.forward, spinSpeed * Time.deltaTime);

        UnityEngine.Debug.Log($"Lip Trill State: {trillState}");
    }

    private void OnCollisionEnter(Collision collision) {
        if (GlobalSettings.gameState == 0) return;
        if (collision.gameObject.CompareTag("Floor")) {
            Instantiate(explosionEffect, transform.position, Quaternion.identity);
            GlobalSettings.sceneManager.EndGame();
            Destroy(gameObject);
        }
        if (collision.gameObject.CompareTag("Hoop")) {
            GlobalSettings.gameState = -1;
        }
    }
    public float getPitch() {
        return targetPitch;
    }
    public bool getTrillState() {
        return trillState;
    }
    public float getBaseTime() {
        return baseTime;
    }
    public int getCurMode() {
        return curMode;
    }


    void DetectPitch() {
        if (!isMicrophoneActive || microphoneClip == null)
            return;

        // Ensure microphone has started
        int micPosition = (int)Microphone.GetPosition(null) - (int)bufferSize;
        if (micPosition < 0)
            return;

        // Retrieve audio data from the microphone
        audioData = new float[bufferSize];
        microphoneClip.GetData(audioData, micPosition);

        float pitch = AubioWrapper.GetPitch(audioData);
        targetPitch = Mathf.Clamp(pitch + GlobalSettings.heightOffset, 0, 150); // Restrict pitch range

    }

    void ReceiveTrillData(string message) {
        try {
            trillState = message.Trim() == "1";
        }
        catch (Exception ex) {
            UnityEngine.Debug.LogError($"Trill UDP Receive Error: {ex.Message}");
        }
    }

    void StartPythonTrillReceiver() {
        try {
            pythonProcess = new Process();
            pythonProcess.StartInfo.FileName = "python3";
            pythonProcess.StartInfo.Arguments = "Audio/trillReceiver.py"; // Ensure the script is in the working directory
            pythonProcess.StartInfo.CreateNoWindow = true;
            pythonProcess.StartInfo.UseShellExecute = false;
            pythonProcess.StartInfo.RedirectStandardOutput = true;
            pythonProcess.StartInfo.RedirectStandardError = true;
            pythonProcess.Start();

            UnityEngine.Debug.Log("Started trillReceiver Python script.");
        } catch (Exception ex) {
            UnityEngine.Debug.LogError($"Failed to start Python script: {ex.Message}");
        }
    }

    void OnApplicationQuit() {
        // Kill the Python script process
        if (pythonProcess != null && !pythonProcess.HasExited) {
            pythonProcess.Kill();
            UnityEngine.Debug.Log("Stopped trillReceiver Python script.");
        }

        trillReceiver.Stop();
        AubioWrapper.CleanUp();
    }

}