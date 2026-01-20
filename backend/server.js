const express = require('express');
const http = require('http');
const { WebSocketServer } = require('ws');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

const app = express();
app.use(express.json({ limit: '2mb' }));

const dataDir = path.join(__dirname, 'data');
const dataFile = path.join(dataDir, 'raffles.json');
let raffles = new Map();
let saveTimer = null;

function loadRaffles() {
  if (!fs.existsSync(dataFile)) {
    return;
  }
  try {
    const raw = fs.readFileSync(dataFile, 'utf8');
    const data = JSON.parse(raw);
    raffles = new Map(data.map((raffle) => [raffle.id, raffle]));
  } catch (err) {
    console.error('Failed to load raffles:', err.message);
  }
}

function scheduleSave() {
  if (saveTimer) {
    return;
  }
  saveTimer = setTimeout(() => {
    saveTimer = null;
    saveRaffles();
  }, 1000);
}

function saveRaffles() {
  try {
    fs.mkdirSync(dataDir, { recursive: true });
    const payload = Array.from(raffles.values());
    fs.writeFileSync(dataFile, JSON.stringify(payload, null, 2), 'utf8');
  } catch (err) {
    console.error('Failed to save raffles:', err.message);
  }
}

function shuffle(items) {
  for (let i = items.length - 1; i > 0; i -= 1) {
    const j = crypto.randomInt(0, i + 1);
    [items[i], items[j]] = [items[j], items[i]];
  }
  return items;
}

function hashTickets(tickets) {
  return tickets.slice().sort().join('|');
}

function buildPublicState(raffle) {
  return {
    raffleId: raffle.id,
    name: raffle.name,
    tickets: raffle.tickets,
    rotation: raffle.rotation || 0,
    winnerName: raffle.winnerName || null,
  };
}

function upsertRaffle(payload, baseUrl) {
  const now = new Date().toISOString();
  const raffleId = payload.raffleId || crypto.randomUUID();
  const existing = raffles.get(raffleId);
  const tickets = Array.isArray(payload.tickets) ? payload.tickets.map(String) : [];
  const ticketHash = hashTickets(tickets);
  const ticketsChanged = !existing || existing.ticketHash !== ticketHash;
  const shuffledTickets = ticketsChanged ? shuffle([...tickets]) : existing.tickets;
  const hostToken = existing?.hostToken || crypto.randomUUID();
  const viewerToken = existing?.viewerToken || crypto.randomUUID();

  const rotation = ticketsChanged ? 0 : existing?.rotation || 0;
  const winnerName = ticketsChanged ? null : existing?.winnerName || null;

  const raffle = {
    id: raffleId,
    name: payload.name || `Raffle ${raffleId.slice(0, 6)}`,
    createdAt: payload.createdAt || now,
    settings: payload.settings || {},
    participants: payload.participants || [],
    tickets: shuffledTickets,
    ticketHash,
    hostToken,
    viewerToken,
    winnerName,
    rotation,
    updatedAt: now,
  };

  raffles.set(raffleId, raffle);
  scheduleSave();

  return {
    raffle,
    hostUrl: `${baseUrl}/host/${raffleId}/${hostToken}`,
    viewerUrl: `${baseUrl}/view/${raffleId}/${viewerToken}`,
  };
}

app.post('/api/raffles', (req, res) => {
  const baseUrl = `${req.protocol}://${req.get('host')}`;
  const result = upsertRaffle(req.body || {}, baseUrl);
  broadcastState(result.raffle.id);
  res.json({
    raffleId: result.raffle.id,
    hostUrl: result.hostUrl,
    viewerUrl: result.viewerUrl,
    winnerName: result.raffle.winnerName,
  });
});

app.get('/api/raffles/:id', (req, res) => {
  const raffle = raffles.get(req.params.id);
  if (!raffle) {
    res.status(404).json({ error: 'Raffle not found.' });
    return;
  }

  const token = req.query.token;
  if (token && token !== raffle.hostToken && token !== raffle.viewerToken) {
    res.status(403).json({ error: 'Invalid token.' });
    return;
  }

  res.json({
    raffleId: raffle.id,
    name: raffle.name,
    winnerName: raffle.winnerName || null,
    tickets: raffle.tickets,
  });
});

app.get('/host/:raffleId/:token', (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'host.html'));
});

app.get('/view/:raffleId/:token', (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'view.html'));
});

app.use(express.static(path.join(__dirname, 'public')));

const server = http.createServer(app);
const wss = new WebSocketServer({ server, path: '/ws' });
const connections = new Map();

function registerConnection(raffleId, ws) {
  if (!connections.has(raffleId)) {
    connections.set(raffleId, new Set());
  }
  connections.get(raffleId).add(ws);
}

function broadcastState(raffleId) {
  const raffle = raffles.get(raffleId);
  if (!raffle || !connections.has(raffleId)) {
    return;
  }
  const message = JSON.stringify({ type: 'updated', ...buildPublicState(raffle) });
  for (const socket of connections.get(raffleId)) {
    socket.send(message);
  }
}

function broadcastSpin(raffleId, payload) {
  if (!connections.has(raffleId)) {
    return;
  }
  const message = JSON.stringify({ type: 'spin', ...payload });
  for (const socket of connections.get(raffleId)) {
    socket.send(message);
  }
}

function spinRaffle(raffle) {
  if (!raffle.tickets.length) {
    return null;
  }
  const ticketCount = raffle.tickets.length;
  const winnerIndex = crypto.randomInt(0, ticketCount);
  const step = (Math.PI * 2) / ticketCount;
  const alignAngle = -Math.PI / 2;
  const baseRotation = alignAngle - (winnerIndex + 0.5) * step;
  const turns = 3 + crypto.randomInt(0, 4);

  let targetRotation = baseRotation;
  while (targetRotation <= raffle.rotation) {
    targetRotation += Math.PI * 2;
  }
  targetRotation += Math.PI * 2 * turns;

  const durationMs = 5000 + crypto.randomInt(0, 3000);
  raffle.rotation = targetRotation;
  raffle.winnerName = raffle.tickets[winnerIndex];
  raffle.updatedAt = new Date().toISOString();
  scheduleSave();

  return {
    winnerIndex,
    winnerName: raffle.winnerName,
    rotation: targetRotation,
    durationMs,
  };
}

wss.on('connection', (ws) => {
  ws.on('message', (data) => {
    let message;
    try {
      message = JSON.parse(data.toString());
    } catch (err) {
      ws.send(JSON.stringify({ type: 'error', message: 'Invalid JSON.' }));
      return;
    }

    if (message.type === 'join') {
      const raffle = raffles.get(message.raffleId);
      if (!raffle) {
        ws.send(JSON.stringify({ type: 'error', message: 'Raffle not found.' }));
        return;
      }

      const token = message.token;
      if (message.role === 'host' && token !== raffle.hostToken) {
        ws.send(JSON.stringify({ type: 'error', message: 'Host token rejected.' }));
        return;
      }

      if (message.role !== 'host' && token !== raffle.viewerToken && token !== raffle.hostToken) {
        ws.send(JSON.stringify({ type: 'error', message: 'Viewer token rejected.' }));
        return;
      }

      ws.raffleId = raffle.id;
      ws.role = message.role;
      registerConnection(raffle.id, ws);
      ws.send(JSON.stringify({ type: 'state', ...buildPublicState(raffle) }));
      return;
    }

    if (message.type === 'spin') {
      const raffleId = ws.raffleId;
      if (!raffleId) {
        ws.send(JSON.stringify({ type: 'error', message: 'Join a raffle first.' }));
        return;
      }

      const raffle = raffles.get(raffleId);
      if (!raffle) {
        ws.send(JSON.stringify({ type: 'error', message: 'Raffle not found.' }));
        return;
      }

      if (ws.role !== 'host') {
        ws.send(JSON.stringify({ type: 'error', message: 'Only the host can spin.' }));
        return;
      }

      const spinResult = spinRaffle(raffle);
      if (!spinResult) {
        ws.send(JSON.stringify({ type: 'error', message: 'No tickets to spin.' }));
        return;
      }

      broadcastSpin(raffleId, spinResult);
      return;
    }
  });

  ws.on('close', () => {
    if (!ws.raffleId || !connections.has(ws.raffleId)) {
      return;
    }
    const set = connections.get(ws.raffleId);
    set.delete(ws);
  });
});

const port = process.env.PORT || 3000;
loadRaffles();
server.listen(port, () => {
  console.log(`FFXIV Raffle 4 All backend running on port ${port}`);
});
