# ArtaloBot WhatsApp Bridge

This is a Node.js bridge that connects ArtaloBot to WhatsApp using the Baileys library.

## Prerequisites

- Node.js 18 or higher
- npm or yarn

## Setup

1. Install dependencies:
```bash
cd src/ArtaloBot.WhatsAppBridge
npm install
```

2. Start the bridge:
```bash
npm start
```

The bridge will start on port 3847 by default.

## Usage

1. Start the WhatsApp Bridge first (`npm start`)
2. Launch ArtaloBot
3. Go to the **Channels** page
4. Click **Connect** on WhatsApp
5. Scan the QR code with your phone:
   - Open WhatsApp on your phone
   - Go to Settings > Linked Devices
   - Tap "Link a Device"
   - Scan the QR code displayed in ArtaloBot

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/status` | GET | Get connection status |
| `/qr` | GET | Get QR code (base64 PNG) |
| `/connect` | POST | Initialize WhatsApp connection |
| `/disconnect` | POST | Disconnect and logout |
| `/send` | POST | Send a message |
| `/messages` | GET | Poll for new messages |

## Environment Variables

- `PORT` - Server port (default: 3847)

## Notes

- Authentication data is stored in the `auth_info` directory
- Delete `auth_info` folder to reset authentication
- The bridge auto-reconnects if session exists
- Messages are processed by ArtaloBot's local Ollama AI

## Troubleshooting

### "Connection closed" errors
- Make sure WhatsApp is up to date on your phone
- Try deleting `auth_info` folder and scanning QR again

### Bridge won't start
- Make sure port 3847 is not in use
- Check Node.js version: `node --version` (should be 18+)

### QR code not showing
- Wait a few seconds for the QR to generate
- Check the console for errors
- Try restarting the bridge
