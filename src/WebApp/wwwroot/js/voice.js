window.voiceInterop = (function () {
  let mediaRecorder = null;
  let chunks = [];
  let mediaStream = null;

  function isSupported() {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder);
  }

  async function startRecording() {
    if (!isSupported()) {
      throw new Error("Microphone recording is not supported in this browser.");
    }

    if (mediaRecorder && mediaRecorder.state === "recording") {
      return;
    }

    chunks = [];
    mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });

    const mimeType = MediaRecorder.isTypeSupported("audio/webm;codecs=opus")
      ? "audio/webm;codecs=opus"
      : MediaRecorder.isTypeSupported("audio/webm")
        ? "audio/webm"
        : "";

    mediaRecorder = mimeType
      ? new MediaRecorder(mediaStream, { mimeType: mimeType })
      : new MediaRecorder(mediaStream);

    mediaRecorder.ondataavailable = function (event) {
      if (event.data && event.data.size > 0) {
        chunks.push(event.data);
      }
    };

    mediaRecorder.start();
  }

  function stopTracks() {
    if (mediaStream) {
      mediaStream.getTracks().forEach(function (track) {
        track.stop();
      });
      mediaStream = null;
    }
  }

  function cancelRecording() {
    if (mediaRecorder && mediaRecorder.state !== "inactive") {
      try {
        mediaRecorder.stop();
      } catch (e) {
        // ignore
      }
    }
    mediaRecorder = null;
    chunks = [];
    stopTracks();
  }

  function blobToBase64(blob) {
    return new Promise(function (resolve, reject) {
      const reader = new FileReader();
      reader.onloadend = function () {
        const result = reader.result || "";
        const comma = result.indexOf(",");
        resolve(comma >= 0 ? result.substring(comma + 1) : result);
      };
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });
  }

  function stopRecording() {
    return new Promise(function (resolve, reject) {
      if (!mediaRecorder || mediaRecorder.state === "inactive") {
        stopTracks();
        reject(new Error("Recording is not active."));
        return;
      }

      const recorder = mediaRecorder;
      recorder.onstop = async function () {
        try {
          const type = recorder.mimeType || "audio/webm";
          const blob = new Blob(chunks, { type: type });
          chunks = [];
          mediaRecorder = null;
          stopTracks();

          if (!blob.size) {
            reject(new Error("No audio was captured."));
            return;
          }

          const base64 = await blobToBase64(blob);
          resolve({
            base64: base64,
            contentType: type.split(";")[0],
            fileName: "recording.webm"
          });
        } catch (err) {
          reject(err);
        }
      };

      recorder.stop();
    });
  }

  return {
    isSupported: isSupported,
    startRecording: startRecording,
    stopRecording: stopRecording,
    cancelRecording: cancelRecording
  };
})();
