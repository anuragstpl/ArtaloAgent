/**
 * ArtaloBot WhatsApp Bridge
 * Uses Baileys to connect to WhatsApp Web and expose REST/WebSocket endpoints
 */

const express = require('express');
const cors = require('cors');
const QRCode = require('qrcode');
const pino = require('pino');
const {
    default: makeWASocket,
    useMultiFileAuthState,
    DisconnectReason,
    fetchLatestBaileysVersion
} = require('@whiskeysockets/baileys');
const path = require('path');
const fs = require('fs');

const app = express();
app.use(cors());
app.use(express.json());

const PORT = process.env.PORT || 3847;
const AUTH_DIR = path.join(__dirname, 'auth_info');

// Logger
const logger = pino({ level: 'info' });

// State
let sock = null;
let qrCode = null;
let connectionStatus = 'disconnected';
let connectedNumber = null;
let messageCallbacks = [];

// Ensure auth directory exists
if (!fs.existsSync(AUTH_DIR)) {
    fs.mkdirSync(AUTH_DIR, { recursive: true });
}

/**
 * Initialize WhatsApp connection
 */
async function connectWhatsApp() {
    try {
        const { state, saveCreds } = await useMultiFileAuthState(AUTH_DIR);
        const { version } = await fetchLatestBaileysVersion();

        sock = makeWASocket({
            version,
            auth: state,
            printQRInTerminal: true,
            logger: pino({ level: 'silent' }),
            browser: ['ArtaloBot', 'Chrome', '1.0.0']
        });

        // Handle connection updates
        sock.ev.on('connection.update', async (update) => {
            const { connection, lastDisconnect, qr } = update;

            if (qr) {
                // Generate QR code as base64 image
                qrCode = await QRCode.toDataURL(qr);
                connectionStatus = 'waiting_for_scan';
                logger.info('QR Code generated, waiting for scan...');
            }

            if (connection === 'close') {
                const shouldReconnect = lastDisconnect?.error?.output?.statusCode !== DisconnectReason.loggedOut;
                logger.info(`Connection closed. Reconnecting: ${shouldReconnect}`);

                connectionStatus = 'disconnected';
                qrCode = null;
                connectedNumber = null;

                if (shouldReconnect) {
                    setTimeout(connectWhatsApp, 3000);
                }
            } else if (connection === 'open') {
                connectionStatus = 'connected';
                qrCode = null;
                connectedNumber = sock.user?.id?.split(':')[0] || 'Unknown';
                logger.info(`Connected as ${connectedNumber}`);
            }
        });

        // Handle credential updates
        sock.ev.on('creds.update', saveCreds);

        // Handle incoming messages
        sock.ev.on('messages.upsert', async ({ messages, type }) => {
            if (type !== 'notify') return;

            for (const msg of messages) {
                // Ignore status broadcasts and own messages
                if (msg.key.remoteJid === 'status@broadcast') continue;
                if (msg.key.fromMe) continue;

                const senderJid = msg.key.remoteJid;
                const senderId = senderJid.replace('@s.whatsapp.net', '').replace('@g.us', '');
                const senderName = msg.pushName || senderId;
                const content = msg.message?.conversation ||
                    msg.message?.extendedTextMessage?.text ||
                    msg.message?.imageMessage?.caption ||
                    '';

                if (!content) continue;

                logger.info(`Message from ${senderName} (${senderId}): ${content}`);

                // Notify all callbacks
                const messageData = {
                    senderId,
                    senderJid,
                    senderName,
                    content,
                    timestamp: new Date().toISOString(),
                    isGroup: senderJid.endsWith('@g.us')
                };

                // Forward to ArtaloBot
                notifyMessageReceived(messageData);
            }
        });

        logger.info('WhatsApp socket initialized');
    } catch (error) {
        logger.error('Failed to connect WhatsApp:', error);
        connectionStatus = 'error';
    }
}

/**
 * Notify ArtaloBot about incoming message
 */
function notifyMessageReceived(messageData) {
    // Store for polling endpoint
    messageCallbacks.push(messageData);

    // Keep only last 100 messages
    if (messageCallbacks.length > 100) {
        messageCallbacks = messageCallbacks.slice(-100);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// REST API Endpoints
// ═══════════════════════════════════════════════════════════════════════════

/**
 * GET /status - Get connection status
 */
app.get('/status', (req, res) => {
    res.json({
        status: connectionStatus,
        connectedNumber,
        hasQrCode: !!qrCode
    });
});

/**
 * GET /qr - Get QR code for scanning
 */
app.get('/qr', (req, res) => {
    if (!qrCode) {
        return res.status(404).json({ error: 'No QR code available' });
    }
    res.json({ qrCode });
});

/**
 * POST /connect - Initialize connection (generate QR)
 */
app.post('/connect', async (req, res) => {
    if (connectionStatus === 'connected') {
        return res.json({ status: 'already_connected', connectedNumber });
    }

    try {
        await connectWhatsApp();
        res.json({ status: 'initializing' });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

/**
 * POST /disconnect - Disconnect and logout
 */
app.post('/disconnect', async (req, res) => {
    try {
        if (sock) {
            await sock.logout();
            sock = null;
        }

        // Clear auth data
        if (fs.existsSync(AUTH_DIR)) {
            fs.rmSync(AUTH_DIR, { recursive: true, force: true });
            fs.mkdirSync(AUTH_DIR, { recursive: true });
        }

        connectionStatus = 'disconnected';
        qrCode = null;
        connectedNumber = null;

        res.json({ status: 'disconnected' });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

/**
 * POST /send - Send a message
 */
app.post('/send', async (req, res) => {
    const { recipientId, message } = req.body;

    if (!recipientId || !message) {
        return res.status(400).json({ error: 'recipientId and message are required' });
    }

    if (connectionStatus !== 'connected' || !sock) {
        return res.status(400).json({ error: 'WhatsApp is not connected' });
    }

    try {
        // Format JID
        const jid = recipientId.includes('@') ? recipientId : `${recipientId}@s.whatsapp.net`;

        await sock.sendMessage(jid, { text: message });

        logger.info(`Message sent to ${recipientId}: ${message.substring(0, 50)}...`);
        res.json({ success: true });
    } catch (error) {
        logger.error('Failed to send message:', error);
        res.status(500).json({ error: error.message });
    }
});

/**
 * GET /messages - Poll for new messages
 */
app.get('/messages', (req, res) => {
    const messages = [...messageCallbacks];
    messageCallbacks = []; // Clear after reading
    res.json({ messages });
});

/**
 * GET /health - Health check
 */
app.get('/health', (req, res) => {
    res.json({
        healthy: true,
        uptime: process.uptime(),
        connectionStatus
    });
});

// ═══════════════════════════════════════════════════════════════════════════
// Start Server
// ═══════════════════════════════════════════════════════════════════════════

app.listen(PORT, () => {
    logger.info(`WhatsApp Bridge running on http://localhost:${PORT}`);
    logger.info('Endpoints:');
    logger.info('  GET  /status     - Connection status');
    logger.info('  GET  /qr         - Get QR code (base64)');
    logger.info('  POST /connect    - Start connection');
    logger.info('  POST /disconnect - Disconnect');
    logger.info('  POST /send       - Send message');
    logger.info('  GET  /messages   - Poll for messages');
});

// Auto-reconnect if auth exists
if (fs.existsSync(path.join(AUTH_DIR, 'creds.json'))) {
    logger.info('Found existing auth, attempting to reconnect...');
    connectWhatsApp();
}
