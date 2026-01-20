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
    pendingWinnerName: null,
    pendingWinnerIndex: null,
    highlightIndex: null,
    highlightStart: 0,
    highlightDuration: 0,
    winnerTimer: null,
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
      resetSpinEffects();
      state.tickets = message.tickets || [];
      state.rotation = message.rotation || 0;
      updateWinner(message.winnerName);
      updateTicketCount();
      drawWheel();
      setStatus("Connected.");
    }

    if (message.type === "updated") {
      resetSpinEffects();
      state.tickets = message.tickets || [];
      state.rotation = message.rotation || 0;
      updateWinner(message.winnerName);
      updateTicketCount();
      drawWheel();
      setStatus("Raffle updated.");
    }

    if (message.type === "spin") {
      queueWinner(message.winnerName, message.winnerIndex);
      animateSpin(message.rotation, message.durationMs, () => {
        revealWinnerAfterHighlight();
      });
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

  function resetSpinEffects() {
    if (state.winnerTimer) {
      clearTimeout(state.winnerTimer);
      state.winnerTimer = null;
    }
    state.pendingWinnerName = null;
    state.pendingWinnerIndex = null;
    state.highlightIndex = null;
    state.highlightStart = 0;
    state.highlightDuration = 0;
  }

  function queueWinner(name, index) {
    if (state.winnerTimer) {
      clearTimeout(state.winnerTimer);
      state.winnerTimer = null;
    }
    state.pendingWinnerName = name || null;
    const parsedIndex = Number.isFinite(Number(index)) ? Number(index) : null;
    if (Number.isInteger(parsedIndex)) {
      state.pendingWinnerIndex = parsedIndex;
    } else if (name) {
      state.pendingWinnerIndex = state.tickets.indexOf(name);
    } else {
      state.pendingWinnerIndex = null;
    }
    updateWinner(null);
  }

  function revealWinnerAfterHighlight() {
    const delayMs = 3000;
    if (Number.isInteger(state.pendingWinnerIndex)) {
      state.highlightIndex = state.pendingWinnerIndex;
      state.highlightStart = performance.now();
      state.highlightDuration = delayMs;
      animateHighlight();
    }

    state.winnerTimer = setTimeout(() => {
      updateWinner(state.pendingWinnerName);
      state.highlightIndex = null;
      state.highlightStart = 0;
      state.highlightDuration = 0;
      state.pendingWinnerName = null;
      state.pendingWinnerIndex = null;
      drawWheel();
    }, delayMs);
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
    const now = performance.now();
    const highlightPop = getHighlightPop(now, radius);

    for (let i = 0; i < state.tickets.length; i += 1) {
      const start = state.rotation + i * step;
      const end = start + step;
      const angle = start + step / 2;
      const isHighlight = highlightPop > 0 && state.highlightIndex === i;
      const popOffset = isHighlight ? highlightPop * 0.35 : 0;
      const wedgeRadius = isHighlight ? radius + highlightPop : radius;
      const wedgeCenterX = center + Math.cos(angle) * popOffset;
      const wedgeCenterY = center + Math.sin(angle) * popOffset;

      ctx.beginPath();
      ctx.moveTo(wedgeCenterX, wedgeCenterY);
      ctx.arc(wedgeCenterX, wedgeCenterY, wedgeRadius, start, end);
      ctx.closePath();
      ctx.fillStyle = colors[i % colors.length];
      ctx.fill();
      ctx.lineWidth = 2;
      ctx.strokeStyle = borderColor;
      ctx.stroke();

      drawLabel(state.tickets[i], wedgeCenterX, wedgeCenterY, wedgeRadius, angle, step, textColor, isHighlight);
    }
  }

  function drawLabel(text, centerX, centerY, radius, angle, step, textColor, isHighlight) {
    const fontFamily = getWheelFont();
    const arcLength = radius * step;
    let fontSize = Math.max(10, Math.min(34, arcLength * 0.7));
    if (isHighlight) {
      fontSize = Math.min(44, fontSize * 1.4);
    }
    ctx.save();
    ctx.translate(centerX, centerY);
    ctx.rotate(angle);
    ctx.translate(radius * 0.86, 0);
    ctx.textAlign = "right";
    ctx.textBaseline = "middle";
    ctx.fillStyle = textColor;
    ctx.font = `${fontSize}px ${fontFamily}`;

    const maxWidth = radius * 0.8;
    const measured = ctx.measureText(text);
    if (measured.width > maxWidth) {
      fontSize = Math.max(8, fontSize * (maxWidth / measured.width));
      ctx.font = `${fontSize}px ${fontFamily}`;
    }

    ctx.fillText(text, 0, 0);
    ctx.restore();
  }

  function getHighlightPop(now, radius) {
    if (state.highlightIndex === null || state.highlightStart <= 0 || state.highlightDuration <= 0) {
      return 0;
    }
    const elapsed = now - state.highlightStart;
    if (elapsed > state.highlightDuration) {
      return 0;
    }
    const eased = easeOutCubic(Math.min(1, elapsed / 300));
    return radius * 0.2 * eased;
  }

  function animateHighlight() {
    if (state.highlightIndex === null || state.highlightStart <= 0 || state.highlightDuration <= 0) {
      return;
    }
    drawWheel();
    const elapsed = performance.now() - state.highlightStart;
    if (elapsed < state.highlightDuration) {
      requestAnimationFrame(animateHighlight);
    }
  }

  function animateSpin(targetRotation, durationMs, onComplete) {
    const startRotation = state.rotation;
    const startTime = performance.now();
    const duration = Math.max(5000, durationMs || 6000);
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
        if (onComplete) {
          onComplete();
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
