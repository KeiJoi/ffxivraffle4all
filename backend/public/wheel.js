(() => {
  const canvas = document.getElementById("wheel");
  const ctx = canvas.getContext("2d");
  const spinButton = document.getElementById("spinButton");
  const statusEl = document.getElementById("status");
  const winnerEl = document.getElementById("winnerName");
  const ticketCountEl = document.getElementById("ticketCount");
  const raffleMeta = document.getElementById("raffleMeta");

  const state = {
    tickets: [],
    rotation: 0,
    spinning: false,
    connected: false,
    role: window.RAFFLE_ROLE || "viewer",
  };

  const pathParts = window.location.pathname.split("/").filter(Boolean);
  const raffleId = pathParts[1] || "";
  const token = pathParts[2] || "";

  if (raffleMeta && raffleId) {
    raffleMeta.textContent = `${state.role === "host" ? "Host" : "Viewer"} view - ${raffleId}`;
  }

  if (!raffleId || !token) {
    setStatus("Invalid raffle link.");
    if (spinButton) {
      spinButton.disabled = true;
    }
    return;
  }

  const wsProtocol = window.location.protocol === "https:" ? "wss" : "ws";
  const ws = new WebSocket(`${wsProtocol}://${window.location.host}/ws`);

  ws.addEventListener("open", () => {
    ws.send(JSON.stringify({
      type: "join",
      raffleId,
      token,
      role: state.role,
    }));
  });

  ws.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.type === "state") {
      state.tickets = message.tickets || [];
      state.rotation = message.rotation || 0;
      updateWinner(message.winnerName);
      updateTicketCount();
      drawWheel();
      setStatus("Connected.");
    }

    if (message.type === "updated") {
      state.tickets = message.tickets || [];
      state.rotation = message.rotation || 0;
      updateWinner(message.winnerName);
      updateTicketCount();
      drawWheel();
      setStatus("Raffle updated.");
    }

    if (message.type === "spin") {
      updateWinner(message.winnerName);
      animateSpin(message.rotation, message.durationMs);
    }

    if (message.type === "error") {
      setStatus(message.message || "Error.");
    }
  });

  ws.addEventListener("close", () => {
    setStatus("Disconnected from server.");
  });

  if (spinButton) {
    spinButton.addEventListener("click", () => {
      if (state.spinning || state.tickets.length === 0) {
        return;
      }
      ws.send(JSON.stringify({ type: "spin" }));
    });
  }

  function setStatus(text) {
    if (statusEl) {
      statusEl.textContent = text;
    }
  }

  function updateWinner(name) {
    if (winnerEl) {
      winnerEl.textContent = name || "-";
    }
  }

  function updateTicketCount() {
    if (ticketCountEl) {
      ticketCountEl.textContent = `Tickets: ${state.tickets.length}`;
    }
    if (spinButton && !state.spinning) {
      spinButton.disabled = state.tickets.length === 0;
    }
  }

  function resizeCanvas() {
    const rect = canvas.getBoundingClientRect();
    const scale = window.devicePixelRatio || 1;
    canvas.width = rect.width * scale;
    canvas.height = rect.height * scale;
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    drawWheel();
  }

  function drawWheel() {
    const rect = canvas.getBoundingClientRect();
    const size = Math.min(rect.width, rect.height);
    const center = size / 2;
    const radius = center - 8;

    ctx.clearRect(0, 0, rect.width, rect.height);

    if (!state.tickets.length) {
      ctx.fillStyle = "rgba(255,255,255,0.2)";
      ctx.beginPath();
      ctx.arc(center, center, radius, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = "rgba(0,0,0,0.7)";
      ctx.font = `20px ${getWheelFont()}`;
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText("No tickets yet", center, center);
      return;
    }

    const colors = getWheelColors();
    const textColor = getWheelTextColor();
    const borderColor = getWheelBorderColor();
    const step = (Math.PI * 2) / state.tickets.length;

    for (let i = 0; i < state.tickets.length; i += 1) {
      const start = state.rotation + i * step;
      const end = start + step;
      ctx.beginPath();
      ctx.moveTo(center, center);
      ctx.arc(center, center, radius, start, end);
      ctx.closePath();
      ctx.fillStyle = colors[i % colors.length];
      ctx.fill();
      ctx.lineWidth = 2;
      ctx.strokeStyle = borderColor;
      ctx.stroke();

      const angle = start + step / 2;
      drawLabel(state.tickets[i], center, radius, angle, step, textColor);
    }
  }

  function drawLabel(text, center, radius, angle, step, textColor) {
    const fontFamily = getWheelFont();
    let fontSize = Math.max(10, Math.min(28, radius * step * 0.85));
    ctx.save();
    ctx.translate(center, center);
    ctx.rotate(angle);
    ctx.translate(radius * 0.62, 0);
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillStyle = textColor;
    ctx.font = `${fontSize}px ${fontFamily}`;

    const maxWidth = radius * 0.7;
    const measured = ctx.measureText(text);
    if (measured.width > maxWidth) {
      fontSize = Math.max(8, fontSize * (maxWidth / measured.width));
      ctx.font = `${fontSize}px ${fontFamily}`;
    }

    ctx.fillText(text, 0, 0);
    ctx.restore();
  }

  function animateSpin(targetRotation, durationMs) {
    const startRotation = state.rotation;
    const startTime = performance.now();
    const duration = Math.max(1000, durationMs || 6000);
    state.spinning = true;
    if (spinButton) {
      spinButton.disabled = true;
    }

    const tick = (now) => {
      const elapsed = now - startTime;
      const progress = Math.min(1, elapsed / duration);
      const eased = easeOutCubic(progress);
      state.rotation = startRotation + (targetRotation - startRotation) * eased;
      drawWheel();
      if (progress < 1) {
        requestAnimationFrame(tick);
      } else {
        state.spinning = false;
        if (spinButton) {
          spinButton.disabled = false;
        }
      }
    };

    requestAnimationFrame(tick);
  }

  function easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
  }

  function getWheelColors() {
    const value = getComputedStyle(document.documentElement).getPropertyValue("--wheel-colors");
    const colors = value.split(",").map((color) => color.trim()).filter(Boolean);
    return colors.length ? colors : ["#f94144", "#f3722c", "#f9c74f", "#90be6d", "#577590"];
  }

  function getWheelBorderColor() {
    const value = getComputedStyle(document.documentElement).getPropertyValue("--wheel-border").trim();
    return value || "#ffffff";
  }

  function getWheelTextColor() {
    const value = getComputedStyle(document.documentElement).getPropertyValue("--wheel-text").trim();
    return value || "#111111";
  }

  function getWheelFont() {
    const value = getComputedStyle(document.documentElement).getPropertyValue("--wheel-font").trim();
    return value || "sans-serif";
  }

  window.addEventListener("resize", resizeCanvas);
  resizeCanvas();
})();
