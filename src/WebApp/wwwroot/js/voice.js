window.voiceInterop = (function () {
  const MAX_DURATION_MS = 12000;
  const VAD_SILENCE_MS = 1200;
  const TIMESLICE_MS = 400;
  const VAD_THRESHOLD = 0.018;

  let mediaRecorder = null;
  let chunks = [];
  let mediaStream = null;
  let netRef = null;
  let audioCtx = null;
  let analyser = null;
  let processor = null;
  let pcmSource = null;
  let vadRaf = null;
  let maxTimer = null;
  let recognition = null;
  let heardSpeech = false;
  let silenceStartedAt = 0;
  let stopping = false;
  let pcmMode = false;
  let pcmSampleRate = 16000;
  let pcmBatchMs = 300;
  let pcmPending = [];
  let pcmPendingSamples = 0;
  let pcmAll = [];
  let liveFinals = "";
  let liveInterim = "";

  function isSupported(pcm) {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      return false;
    }

    if (pcm) {
      return typeof AudioContext !== "undefined" || typeof webkitAudioContext !== "undefined";
    }

    return !!window.MediaRecorder;
  }

  function attach(dotNetHelper) {
    netRef = dotNetHelper;
  }

  function detach() {
    netRef = null;
  }

  function notify(method, arg) {
    if (!netRef) {
      return;
    }

    const promise = typeof arg === "undefined"
      ? netRef.invokeMethodAsync(method)
      : netRef.invokeMethodAsync(method, arg);

    promise.catch(function () { /* component disposed */ });
  }

  function blobToBase64(blob) {
    return new Promise(function (resolve, reject) {
      const reader = new FileReader();
      reader.onloadend = function () {
        const result = reader.result || "";
        const comma = result.indexOf(",");
        resolve(comma >= 0 ? result.substring(comma + 1) : String(result));
      };
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });
  }

  function bytesToBase64(bytes) {
    let binary = "";
    const chunk = 0x8000;
    for (let i = 0; i < bytes.length; i += chunk) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
    }
    return btoa(binary);
  }

  function stopTracks() {
    if (mediaStream) {
      mediaStream.getTracks().forEach(function (track) {
        track.stop();
      });
      mediaStream = null;
    }
  }

  function stopPcmCapture() {
    if (processor) {
      try { processor.disconnect(); } catch (e) { /* ignore */ }
      processor.onaudioprocess = null;
      processor = null;
    }
    if (pcmSource) {
      try { pcmSource.disconnect(); } catch (e) { /* ignore */ }
      pcmSource = null;
    }
  }

  function stopVad() {
    if (vadRaf) {
      cancelAnimationFrame(vadRaf);
      vadRaf = null;
    }
    analyser = null;
  }

  function closeAudioCtx() {
    if (audioCtx) {
      try { audioCtx.close(); } catch (e) { /* ignore */ }
      audioCtx = null;
    }
  }

  function notifyLive() {
    const finals = (liveFinals || "").trim();
    const interim = (liveInterim || "").trim();
    const text = (finals + (interim ? (finals ? " " : "") + interim : "")).trim();
    notify("OnLiveTranscript", {
      text: text,
      finals: finals,
      hasInterim: !!interim
    });
  }

  function stopLiveSpeech() {
    if (!recognition) {
      return;
    }

    try { recognition.stop(); } catch (e) { /* ignore */ }
    recognition = null;
  }

  function stopLiveSpeechAndWait(timeoutMs) {
    return new Promise(function (resolve) {
      if (!recognition) {
        resolve();
        return;
      }

      const rec = recognition;
      let done = false;
      function finish() {
        if (done) {
          return;
        }
        done = true;
        recognition = null;
        resolve();
      }

      rec.onend = finish;
      try {
        rec.stop();
      } catch (e) {
        finish();
        return;
      }
      setTimeout(finish, timeoutMs || 400);
    });
  }

  function startLiveSpeech() {
    const Ctor = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Ctor) {
      return;
    }

    liveFinals = "";
    liveInterim = "";
    try {
      recognition = new Ctor();
      recognition.continuous = true;
      recognition.interimResults = true;
      recognition.lang = "en-US";
      recognition.onresult = function (event) {
        let finals = "";
        let interim = "";
        for (let i = 0; i < event.results.length; i++) {
          const piece = event.results[i][0] ? event.results[i][0].transcript : "";
          if (event.results[i].isFinal) {
            finals += piece;
          } else {
            interim += piece;
          }
        }
        liveFinals = finals;
        liveInterim = interim;
        notifyLive();
      };
      recognition.onerror = function () { /* ignore; Whisper is fallback */ };
      recognition.start();
    } catch (e) {
      recognition = null;
    }
  }

  function startVad() {
    if (!mediaStream || typeof AudioContext === "undefined" && typeof webkitAudioContext === "undefined") {
      return;
    }

    ensureAudioCtx();
    if (!audioCtx) {
      return;
    }

    const source = audioCtx.createMediaStreamSource(mediaStream);
    analyser = audioCtx.createAnalyser();
    analyser.fftSize = 1024;
    source.connect(analyser);
    heardSpeech = false;
    silenceStartedAt = 0;
    const data = new Uint8Array(analyser.fftSize);

    function tick() {
      if (!analyser) {
        return;
      }

      analyser.getByteTimeDomainData(data);
      let sum = 0;
      for (let i = 0; i < data.length; i++) {
        const v = (data[i] - 128) / 128;
        sum += v * v;
      }
      const rms = Math.sqrt(sum / data.length);
      const now = performance.now();

      if (rms > VAD_THRESHOLD) {
        heardSpeech = true;
        silenceStartedAt = 0;
      } else if (heardSpeech) {
        if (!silenceStartedAt) {
          silenceStartedAt = now;
        } else if (now - silenceStartedAt > VAD_SILENCE_MS) {
          notify("OnVoiceAutoStop");
          return;
        }
      }

      vadRaf = requestAnimationFrame(tick);
    }

    vadRaf = requestAnimationFrame(tick);
  }

  function ensureAudioCtx() {
    if (audioCtx) {
      return audioCtx;
    }

    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) {
      return null;
    }

    audioCtx = new Ctx();
    return audioCtx;
  }

  function downsample(float32, fromRate, toRate) {
    if (fromRate === toRate) {
      return float32;
    }

    const ratio = fromRate / toRate;
    const newLen = Math.max(1, Math.round(float32.length / ratio));
    const result = new Float32Array(newLen);
    for (let i = 0; i < newLen; i++) {
      result[i] = float32[Math.min(float32.length - 1, Math.floor(i * ratio))];
    }
    return result;
  }

  function floatTo16BitPcm(float32) {
    const bytes = new Uint8Array(float32.length * 2);
    const view = new DataView(bytes.buffer);
    for (let i = 0; i < float32.length; i++) {
      let s = Math.max(-1, Math.min(1, float32[i]));
      view.setInt16(i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
    }
    return bytes;
  }

  function concatFloat32(parts) {
    let total = 0;
    for (let i = 0; i < parts.length; i++) {
      total += parts[i].length;
    }
    const out = new Float32Array(total);
    let offset = 0;
    for (let i = 0; i < parts.length; i++) {
      out.set(parts[i], offset);
      offset += parts[i].length;
    }
    return out;
  }

  function flushPcm(force) {
    if (!pcmPendingSamples) {
      return;
    }

    const fromRate = audioCtx ? audioCtx.sampleRate : pcmSampleRate;
    const needed = Math.ceil((pcmBatchMs / 1000) * fromRate);
    if (!force && pcmPendingSamples < needed) {
      return;
    }

    const merged = concatFloat32(pcmPending);
    pcmPending = [];
    pcmPendingSamples = 0;
    const down = downsample(merged, fromRate, pcmSampleRate);
    const pcm = floatTo16BitPcm(down);
    pcmAll.push(pcm);
    notify("OnVoiceChunk", {
      base64: bytesToBase64(pcm),
      contentType: "audio/pcm"
    });
  }

  function concatUint8(parts) {
    let total = 0;
    for (let i = 0; i < parts.length; i++) {
      total += parts[i].length;
    }
    const out = new Uint8Array(total);
    let offset = 0;
    for (let i = 0; i < parts.length; i++) {
      out.set(parts[i], offset);
      offset += parts[i].length;
    }
    return out;
  }

  function pcmToWav(pcmBytes, sampleRate) {
    const header = new ArrayBuffer(44);
    const view = new DataView(header);
    function writeStr(offset, str) {
      for (let i = 0; i < str.length; i++) {
        view.setUint8(offset + i, str.charCodeAt(i));
      }
    }
    writeStr(0, "RIFF");
    view.setUint32(4, 36 + pcmBytes.length, true);
    writeStr(8, "WAVE");
    writeStr(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, 1, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * 2, true);
    view.setUint16(32, 2, true);
    view.setUint16(34, 16, true);
    writeStr(36, "data");
    view.setUint32(40, pcmBytes.length, true);
    const wav = new Uint8Array(44 + pcmBytes.length);
    wav.set(new Uint8Array(header), 0);
    wav.set(pcmBytes, 44);
    return wav;
  }

  function startPcmCapture() {
    ensureAudioCtx();
    if (!audioCtx || !mediaStream) {
      throw new Error("AudioContext is not available for PCM capture.");
    }

    pcmSource = audioCtx.createMediaStreamSource(mediaStream);
    processor = audioCtx.createScriptProcessor(4096, 1, 1);
    const mute = audioCtx.createGain();
    mute.gain.value = 0;
    processor.onaudioprocess = function (event) {
      if (stopping || !processor) {
        return;
      }

      const input = event.inputBuffer.getChannelData(0);
      pcmPending.push(new Float32Array(input));
      pcmPendingSamples += input.length;
      flushPcm(false);
    };
    pcmSource.connect(processor);
    processor.connect(mute);
    mute.connect(audioCtx.destination);
  }

  async function startRecording(options) {
    options = options || {};
    pcmMode = !!options.pcm;
    pcmSampleRate = options.sampleRate > 0 ? options.sampleRate : 16000;
    pcmBatchMs = options.batchMs > 0 ? options.batchMs : 300;

    if (!isSupported(pcmMode)) {
      throw new Error("Microphone recording is not supported in this browser.");
    }

    if (!pcmMode && mediaRecorder && mediaRecorder.state === "recording") {
      return {
        contentType: (mediaRecorder.mimeType || "audio/webm").split(";")[0],
        fileName: "recording.webm"
      };
    }

    stopping = false;
    chunks = [];
    pcmPending = [];
    pcmPendingSamples = 0;
    pcmAll = [];
    liveFinals = "";
    liveInterim = "";
    mediaStream = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true }
    });

    if (audioCtx && audioCtx.state === "suspended") {
      try { await audioCtx.resume(); } catch (e) { /* ignore */ }
    }

    startVad();

    if (pcmMode) {
      startPcmCapture();
      if (maxTimer) {
        clearTimeout(maxTimer);
      }
      maxTimer = setTimeout(function () {
        notify("OnVoiceAutoStop");
      }, MAX_DURATION_MS);

      return {
        contentType: "audio/pcm",
        fileName: "recording.pcm"
      };
    }

    const mimeType = MediaRecorder.isTypeSupported("audio/webm;codecs=opus")
      ? "audio/webm;codecs=opus"
      : MediaRecorder.isTypeSupported("audio/webm")
        ? "audio/webm"
        : "";

    mediaRecorder = mimeType
      ? new MediaRecorder(mediaStream, { mimeType: mimeType })
      : new MediaRecorder(mediaStream);

    mediaRecorder.ondataavailable = async function (event) {
      if (!event.data || event.data.size === 0) {
        return;
      }

      chunks.push(event.data);
      try {
        const base64 = await blobToBase64(event.data);
        notify("OnVoiceChunk", {
          base64: base64,
          contentType: (mediaRecorder && mediaRecorder.mimeType ? mediaRecorder.mimeType : "audio/webm").split(";")[0]
        });
      } catch (e) {
        // ignore chunk send failures; final blob is still captured
      }
    };

    mediaRecorder.start(TIMESLICE_MS);
    startLiveSpeech();

    if (maxTimer) {
      clearTimeout(maxTimer);
    }
    maxTimer = setTimeout(function () {
      notify("OnVoiceAutoStop");
    }, MAX_DURATION_MS);

    return {
      contentType: (mediaRecorder.mimeType || "audio/webm").split(";")[0],
      fileName: "recording.webm"
    };
  }

  function capturePointer(element, pointerId) {
    if (element && element.setPointerCapture) {
      try {
        element.setPointerCapture(pointerId);
      } catch (e) {
        // ignore
      }
    }
  }

  function cancelRecording() {
    stopping = true;
    if (maxTimer) {
      clearTimeout(maxTimer);
      maxTimer = null;
    }
    stopLiveSpeech();
    stopPcmCapture();
    stopVad();
    closeAudioCtx();

    if (mediaRecorder && mediaRecorder.state !== "inactive") {
      try {
        mediaRecorder.stop();
      } catch (e) {
        // ignore
      }
    }
    mediaRecorder = null;
    chunks = [];
    pcmPending = [];
    pcmAll = [];
    stopTracks();
  }

  function livePayload() {
    return {
      liveFinals: (liveFinals || "").trim(),
      liveHasInterim: !!(liveInterim && liveInterim.trim())
    };
  }

  function stopPcmRecording() {
    return new Promise(function (resolve, reject) {
      if (stopping) {
        reject(new Error("Recording is already stopping."));
        return;
      }

      stopping = true;
      if (maxTimer) {
        clearTimeout(maxTimer);
        maxTimer = null;
      }

      stopPcmCapture();
      flushPcm(true);
      stopVad();
      closeAudioCtx();
      stopTracks();
      stopping = false;

      const pcm = concatUint8(pcmAll);
      pcmAll = [];
      pcmPending = [];
      if (!pcm.length) {
        reject(new Error("No audio was captured."));
        return;
      }

      const wav = pcmToWav(pcm, pcmSampleRate);
      resolve({
        base64: bytesToBase64(wav),
        contentType: "audio/wav",
        fileName: "recording.wav",
        liveFinals: "",
        liveHasInterim: false
      });
    });
  }

  function stopRecording() {
    if (pcmMode) {
      return stopPcmRecording();
    }

    return new Promise(function (resolve, reject) {
      if (!mediaRecorder || mediaRecorder.state === "inactive") {
        stopTracks();
        reject(new Error("Recording is not active."));
        return;
      }

      if (stopping) {
        reject(new Error("Recording is already stopping."));
        return;
      }

      stopping = true;
      if (maxTimer) {
        clearTimeout(maxTimer);
        maxTimer = null;
      }
      stopVad();

      const recorder = mediaRecorder;
      recorder.onstop = async function () {
        try {
          const type = recorder.mimeType || "audio/webm";
          const blob = new Blob(chunks, { type: type });
          chunks = [];
          mediaRecorder = null;
          stopTracks();
          closeAudioCtx();
          stopping = false;

          if (!blob.size) {
            reject(new Error("No audio was captured."));
            return;
          }

          const base64 = await blobToBase64(blob);
          const live = livePayload();
          resolve({
            base64: base64,
            contentType: type.split(";")[0],
            fileName: "recording.webm",
            liveFinals: live.liveFinals,
            liveHasInterim: live.liveHasInterim
          });
        } catch (err) {
          stopping = false;
          reject(err);
        }
      };

      stopLiveSpeechAndWait(400).then(function () {
        try {
          recorder.stop();
        } catch (err) {
          stopping = false;
          reject(err);
        }
      });
    });
  }

  return {
    isSupported: isSupported,
    attach: attach,
    detach: detach,
    capturePointer: capturePointer,
    startRecording: startRecording,
    stopRecording: stopRecording,
    cancelRecording: cancelRecording
  };
})();
