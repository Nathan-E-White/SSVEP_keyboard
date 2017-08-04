using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
//using UnityEngine.Microphone;

public class MicrophoneInput : MonoBehaviour {

	public bool readSamplesOn = true;
	public AudioSource audio;
	public AudioSource tone;
	public AudioClip demoTone;					//Demo tone to use in the app
	private float[] samples;					//full range of recorded samples
	private float[] sampleSet;					//small subset of samples used for analysis
	private float[] sampleSetAvg;				//small subset of samples used for analysis
	private float[,] sampleSetPrev;				//saved from previous read
	private float[] sampleSetProcessed;			//processed for interpretation
	private float[,] sampleSetProcessedPrev;	//saved from previous read
	public EQView _eqView;
	public bool useMicrophone = true;
	public bool useClamp = true;
	public float leftClamp = 0.0f;
	public float rightClamp = 1.0f;
	private int sampleRate = 44100;				//44100 typical 
	public int fTarget = 1000;					//Target in HZ
	public int fWidth = 120;					//Width of area of interest around target in HZ
	private int minFreq = 0;
	private int maxFreq = 0;
	public float ssvepLowF = 12.0f;
	public float ssvepHighF = 20.0f;
	private int startValue;
	private int endValue;
	private int sampleSetSize;
	private int[] ssvepLowValues;
	private int[] ssvepHighValues;
	private int numSamplesTaken = 0;
	public bool averageOverTime = false;
	public int maxSamples = 0;					//Number of frames to sample.  Set to 0 for infinite samples
	private int sampleSetCounter = 0;			//Keeps track of location in sampleSetPrev
	private bool useMaxSamples;
	public int sAvgWidth = 1;					//Number of samples to average together. Default: 1 is no averaging
	public int sampleProcessingMode = 0;
	private int numSamples = 8192;				//Must be power of 2  Min: 64, Max: 8192
	private float fMax;							//typically 44100Hz but it has to be read in from the system to be sure. This value can be chaged on iOS or Android but not on Mac or PC
	private FFTWindow specFFTwindow = FFTWindow.Hanning;  //Previously Hanning
	private float diff = 0.0f;
	public int diffTrigger = 0;
	public int triggerTime = 60;				//Number of frames it takes within a range to trigger
	public float triggerHigh = 0.1f;
	public float triggerLow = -0.1f;
	public float triggerResetHigh = 0.001f;
	public float triggerResetLow = -0.001f;
	//public int activeAudioClip = 0;
	//public AudioClip[] audioClips;

	
	void Awake () {
		foreach (string device in Microphone.devices) {
            Debug.Log("Name: " + device);
        }
		InitializeAudio();
	}

	// Update is called once per frame
	void Update () {
		if (readSamplesOn) {
			ReadSamples();
		}
	}

	public void ResetSamples() {
		audio.GetSpectrumData (samples, 0, specFFTwindow);
		for (int i = 0; i < numSamples; i++) {
			samples[i] = Mathf.Abs(samples[i]);
		}
		numSamplesTaken = 0;
		diffTrigger = 0;
	}

	public void NextProcessingMode() {
		sampleProcessingMode = (sampleProcessingMode + 1)%5;
	}

	public void MicToggle() {
		useMicrophone =  !useMicrophone;
		InitializeAudio();
		ResetSamples();
	}

	void InitializeAudio () {

		#if UNITY_IOS
			Debug.Log("iPhone");
			AudioConfiguration config = AudioSettings.GetConfiguration();
			config.sampleRate = 11025;
			AudioSettings.Reset(config);
		#endif

		//Sample Rate on iOS will hopefully be 11025, this should be the same on Android. Mac and PC can only use 48000
		sampleRate = Mathf.FloorToInt(AudioSettings.outputSampleRate);
		Debug.Log("outputSampleRate: " + sampleRate.ToString());

		audio.loop = true;
		//audio.clip.set(audioClips[activeAudioClip]);
		samples = new float[numSamples];

		fMax = AudioSettings.outputSampleRate / 2;
		startValue = Mathf.FloorToInt((fTarget - (fWidth / 2)) * numSamples / fMax);
		endValue = Mathf.FloorToInt((fTarget + (fWidth / 2)) * numSamples / fMax);
		sampleSetSize = endValue - startValue;

		ssvepLowValues = new int[2];
		ssvepLowValues[0] = Mathf.FloorToInt((fTarget - ssvepLowF) * numSamples / fMax) - startValue;
		ssvepLowValues[1] = Mathf.FloorToInt((fTarget + ssvepLowF) * numSamples / fMax) - startValue;

		ssvepHighValues = new int[2];
		ssvepHighValues[0] = Mathf.FloorToInt((fTarget - ssvepHighF) * numSamples / fMax) - startValue;
		ssvepHighValues[1] = Mathf.FloorToInt((fTarget + ssvepHighF) * numSamples / fMax) - startValue;		

		if (maxSamples > 0 ) {
			useMaxSamples = true;
		} else {
			useMaxSamples = false;
			maxSamples = 1;
		}
		sampleSet = new float[sampleSetSize];
		sampleSetPrev = new float[maxSamples,sampleSetSize];
		sampleSetProcessed = new float[sampleSetSize];
		sampleSetProcessedPrev = new float[maxSamples,sampleSetSize];
		sampleSetAvg = new float[sampleSetSize];


		//Debug.Log("fMax: "+fMax.ToString());
		if (useMicrophone) {
			foreach(string s in Microphone.devices) {
				Microphone.GetDeviceCaps(s, out minFreq, out maxFreq);	
				Debug.Log("Device Name: " + s + " [" + minFreq.ToString() + "-" + maxFreq.ToString() + "]");
			}
			//audio.clip = Microphone.Start("Built-in Microphone", true, 10, sampleRate);
			audio.clip = Microphone.Start(null, true, 10, sampleRate);
			while (Microphone.GetPosition(null) <= 0){}
		} else {
			audio.clip = demoTone;
		}
		//audio.mute = true;
		audio.Play();
		audio.GetSpectrumData (samples, 0, specFFTwindow);
	}

	void ReadSamples () {
		// Read in audio data to samples
		audio.GetSpectrumData (samples, 0, specFFTwindow);
		//audio.GetOutputData (samples, 0);

		numSamplesTaken++;

		// Copy a small subset of samples to sampleSet
		System.Array.Copy(samples,startValue,sampleSet,0,sampleSet.Length);

		// Create a processed sample set
		System.Array.Copy(sampleSet,sampleSetProcessed,sampleSet.Length);		

		//sampleSetProcessed = FoldSamples(sampleSetProcessed);
		// Normalize the data
		sampleSetProcessed = NormalizeSamples(sampleSetProcessed);

		// Average the samples over time.  If maxSamples = 1 this will just return the same values
		if (averageOverTime) {
			sampleSetAvg = AverageSamplesTime(sampleSetProcessed, sampleSetAvg, sampleSetPrev, numSamplesTaken, sampleSetCounter, useMaxSamples);
			sampleSetProcessed = sampleSetAvg;
		}

		switch (sampleProcessingMode) {
			case 0:
				//NoProcessSamples();
				break;
			case 1:
				sampleSetProcessed = AverageSamples(sampleSetProcessed, sAvgWidth);
				break;
			case 2:
				sampleSetProcessed = RelativePeakSamples(sampleSetProcessed, sAvgWidth);
				break;
			case 3:
				sampleSetProcessed = PeakSamples(sampleSetProcessed, sAvgWidth);
				break;
			case 4:
				sampleSetProcessed = PeaksOnlySamples(sampleSetProcessed, sAvgWidth);
				break;
		}

		// Boost the data for visual output
		sampleSetProcessed = BoostSamples(sampleSetProcessed);

		//Calculate the bucket numbers for SSVEP low and high values on both ends of the spectrum (even though only the left side is used)
		ssvepLowValues[0] = Mathf.FloorToInt((fTarget - ssvepLowF) * numSamples / fMax) - startValue;
		ssvepLowValues[1] = Mathf.FloorToInt((fTarget + ssvepLowF) * numSamples / fMax) - startValue;
		ssvepHighValues[0] = Mathf.FloorToInt((fTarget - ssvepHighF) * numSamples / fMax) - startValue;
		ssvepHighValues[1] = Mathf.FloorToInt((fTarget + ssvepHighF) * numSamples / fMax) - startValue;

		//Calculate the difference in the height of the low and high frequency peaks.
		diff = sampleSetProcessed[ssvepHighValues[0]] - sampleSetProcessed[ssvepLowValues[0]];
		if (diff>triggerHigh) {
			++diffTrigger;
		} else if (diff<triggerLow) {
			--diffTrigger;
		} else if ((diff<triggerResetHigh) && (diff>triggerResetLow)) {
			//Reset the diffTrigger value if it drops out of range for low or high triggers
			diffTrigger = 0;
		}

		//Debug.Log("Diff: " + diff.ToString("0.0000"));
		//Debug.Log(string.Format("{0:####.000} Hz", diff));
		//Debug.Log("diffTrigger: " + diffTrigger);

		//Flicker the relevant SSVEP high and low values at regular intervals. This happens after the check has taken place.
		if (numSamplesTaken % 5 == 0) {
			sampleSetProcessed[ssvepLowValues[0]] = 0.2f;
			sampleSetProcessed[ssvepLowValues[1]] = 0.2f;
			sampleSetProcessed[ssvepHighValues[0]] = 0.1f;
			sampleSetProcessed[ssvepHighValues[1]] = 0.1f;	
		}
		
		//DrawDebugLines();
		//Debug.Log(sampleSetProcessed[0].ToString());

		if (useClamp) {
			//Used for updating eq with % clamps on either side
			_eqView.UpdateEQClamped(sampleSetProcessed, leftClamp, rightClamp);
		} else {
			_eqView.UpdateEQClamped(sampleSetProcessed, 0.0f, 1.0f);
		}
		
		//Debug.Log(samples[512].ToString());
		// Copy samplesets to previous sample sets for use in averaging
		for (int i = 0; i < sampleSetSize; i++) {
			sampleSetPrev[sampleSetCounter,i] = sampleSet[i];
			//sampleSetProcessedPrev[sampleSetCounter,i] = sampleSetProcessed[i];
		}

		// Increment the counter used to identify which previous sample set to use next
		sampleSetCounter = (sampleSetCounter + 1) % maxSamples;

	}

	static float[] FoldSamples(float[] sIn) {
		float[] sOut = new float[Mathf.CeilToInt(sIn.Length/2)];
		for (int i = 0; i < sOut.Length; i++) {
			sOut[i] = sIn[i] + sIn[sIn.Length-i-1];
		}
		return sOut;
	}

	static float[] NormalizeSamples(float[] sIn) {
		float[] sOut = new float[sIn.Length];
		float maxSample = sIn.Max();
		float minSample = sIn.Min();
		for (int i = 0; i < sIn.Length; i++) {
			sOut[i] = Mathf.InverseLerp(minSample, maxSample, sIn[i]);
		}
		return sOut;
	}

	static float[] BoostSamples(float[] sIn) {
		float[] sOut = new float[sIn.Length];
		for (int i = 0; i < sIn.Length; i++) {
			sOut[i] = Mathf.Sqrt(Mathf.Sqrt(sIn[i]));
		}
		return sOut;
	}

	static float[] AverageSamplesTime(float[] sIn, float[] sAvg, float[,] sPrev, int t, int c, bool useMax) {
		if (t == 1) {
			return sIn;
		}
		float[] sOut = new float[sIn.Length];
		int maxS = sPrev.GetLength(0);
		if ((t < maxS) || !useMax) {
			for (int i = 0; i < sIn.Length; i++) {
				sOut[i] = (sAvg[i] * (t-1) + sIn[i]) / t;
			}
		} else {	
			for (int i = 0; i < sIn.Length; i++) {
				sOut[i] = sAvg[i] + ((sIn[i] - sPrev[c,i]) / maxS);
			}
		}
		return sOut;
	}

	//Each output sample will be an average of itself and w neighbours on each side.
	static float[] AverageSamples(float[] sIn, int w) {
		float[] sOut = new float[sIn.Length];
		for (int i = 0; i < sIn.Length; i++) {
			//sOut[i] = 0.0f;
			for (int j = Mathf.Max(i - w,0); j < Mathf.Min(i + w + 1,sIn.Length); j++) {
				sOut[i] += sIn[j];
			}
			sOut[i] /= Mathf.Min(i + w + 1,sIn.Length) - Mathf.Max(i - w,0);
			sOut[i] = sIn[i] - sOut[i];
		}
		return sOut;
	}

	//Each output sample will be the input sample minus the midpoint between the neighbour vales w away in each direction.
	//Negative values are zeroed as we're only looking for relative peaks.
	static float[] RelativePeakSamples(float[] sIn, int w) {
		float[] sOut = new float[sIn.Length];
		for (int i = 0; i < sIn.Length; i++) {
			sOut[i] = Mathf.Max(sIn[i] - Mathf.Lerp(sIn[Mathf.Max(i-w,0)],sIn[Mathf.Min(i+w,sIn.Length-1)],0.5f),0);
		}
		return sOut;
	}

	//Each output sample will be the input sample minus the midpoint between the neighbour vales w away in each direction.
	//Any value less than either neighbour is zeroed as we're only looking for true peaks.
	static float[] PeakSamples(float[] sIn, int w) {
		float[] sOut = new float[sIn.Length];
		for (int i = 0; i < sIn.Length; i++) {
			if (sIn[i] > sIn[Mathf.Max(i-w,0)] && sIn[i] > sIn[Mathf.Min(i+w,sIn.Length-1)]) {
				//sOut[i] = Mathf.Abs(sIn[i] - Mathf.Lerp(sIn[Mathf.Max(i-w,0)],sIn[Mathf.Min(i+w,sIn.Length-1)],0.5f));
				sOut[i] = sIn[i] - Mathf.Lerp(sIn[Mathf.Max(i-w,0)],sIn[Mathf.Min(i+w,sIn.Length-1)],0.5f);
			} else {
				sOut[i] = 0.0f;
			}
		}
		return sOut;
	}

	//Any peaks (measuring sample against neighbour w away) are output as 1 and all other values are zero
	static float[] PeaksOnlySamples(float[] sIn, int w) {
		float[] sOut = new float[sIn.Length];
		for (int i = 0; i < sIn.Length; i++) {
			if (sIn[i] > sIn[Mathf.Max(i-w,0)] && sIn[i] > sIn[Mathf.Min(i+w,sIn.Length-1)]) {
				sOut[i] = 1.0f;
			} else {
				sOut[i] = 0.0f;
			}
		}
		return sOut;
	}

	void DrawDebugLines () {
		//Debug.Log(numSamplesTaken.ToString());

		int xScale = 8;
		int yScale = 400;
		int xoffset = 3000;
		int yoffset = 1000;
		for (int i = 1; i < numSamples-1; i++) {
		 	Debug.DrawLine (new Vector3 ((i - 1)*xScale-xoffset, samples[i]*yScale-yoffset, 0),
						new Vector3 (i*xScale-xoffset, samples[i + 1]*yScale-yoffset, 0), Color.red);

			Debug.DrawLine (new Vector3 ((i - 1)*xScale-xoffset, Mathf.Log(samples[i])*yScale-yoffset, 0),
						new Vector3 (i*xScale-xoffset, Mathf.Log(samples[i + 1])*yScale-yoffset, 0), Color.cyan);

			Debug.DrawLine (new Vector3 (Mathf.Log(i - 1)*xScale-xoffset, samples[i]*yScale-yoffset, 0),
						new Vector3 (Mathf.Log(i)*xScale-xoffset, samples[i + 1]*yScale-yoffset, 0), Color.green);

			Debug.DrawLine (new Vector3 (Mathf.Log(i - 1)*xScale-xoffset, Mathf.Log(samples[i])*yScale-yoffset, 0),
						new Vector3 (Mathf.Log(i)*xScale-xoffset, Mathf.Log(samples[i + 1])*yScale-yoffset, 0), Color.yellow);
		 }
	}


}
