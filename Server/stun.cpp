#include "stun.h"
#include <algorithm>
#include <chrono>
#include <iostream>
#include <map>
#include <sstream>
#include <cstring>
#include <string>
#include <thread>
#include <vector>
#include <stdexcept>

// STUNServerException implementation
STUNServerException::STUNServerException(const std::string& message)
    : std::runtime_error("STUN Server Error: " + message) {}

// Peer implementation
Peer::Peer() : enetPeer(nullptr), isHost(false), isMatched(false) {}

std::array<uint8_t, 4> Peer::publicIpv4() const {
    if (enetPeer == nullptr) {
        throw STUNServerException("Cannot get IP from null peer");
    }
    
    const uint32_t ip = enetPeer->address.host;
    return {
        static_cast<uint8_t>(ip & 0xFF),
        static_cast<uint8_t>(ip >> 8 & 0xFF),
        static_cast<uint8_t>(ip >> 16 & 0xFF),
        static_cast<uint8_t>(ip >> 24 & 0xFF)
    };
}

// Room implementation
Room::Room() : createdAt(std::chrono::steady_clock::now()) {}

bool Room::hasExpired() const {
    const auto now = std::chrono::steady_clock::now();
    const auto roomAge = std::chrono::duration_cast<std::chrono::hours>(now - createdAt);
    return roomAge >= ROOM_LIFETIME_HOURS;
}

// STUNServer implementation
STUNServer::STUNServer() : server(nullptr), lastCleanupCheck(std::chrono::steady_clock::now()) {}

STUNServer::~STUNServer() {
    cleanup();
}

void STUNServer::initialize() {
    if (enet_initialize() != 0)
        throw STUNServerException("Failed to initialize ENET library");

    ENetAddress address;
    address.host = ENET_HOST_ANY;
    address.port = SERVER_PORT;

    constexpr size_t maxPeers = 32;
    constexpr size_t maxChannels = 2;
    constexpr enet_uint32 maxInBandwidth = 0;
    constexpr enet_uint32 maxOutBandwidth = 0;

    server = enet_host_create(&address, maxPeers, maxChannels, maxInBandwidth, maxOutBandwidth);
    if (server == nullptr) {
        enet_deinitialize();
        throw STUNServerException("Failed to create ENET host on port " + std::to_string(SERVER_PORT));
    }
}

void STUNServer::handlePeerConnect(ENetPeer* peer) {
    // Rate limit connections (more strict: 3 connections per minute)
    static RateLimiter connectionRateLimiter(
        std::chrono::minutes(1l),       // 1 minute window
        5,                                     // Max 5 connections
        std::chrono::minutes(3l)        // Block for 3 minutes
    );
    
    if (!connectionRateLimiter.isAllowed(peer->address.host)) {
        std::cerr << "Connection from " << Int32ToIpv4(peer->address.host)
                  << " blocked due to rate limiting" << std::endl;
        enet_peer_disconnect_now(peer, 0);
        return;
    }

    std::cout << "Peer connected from " << Int32ToIpv4(peer->address.host) << std::endl;
}

void STUNServer::run() {
    ENetEvent event;

    while (true) {
        while (enet_host_service(server, &event, 1000) > 0) {
            switch (event.type) {
                case ENET_EVENT_TYPE_CONNECT:
                    handlePeerConnect(event.peer);
                    break;
                case ENET_EVENT_TYPE_RECEIVE:
                    handlePeerMessage(event.peer, event.packet);
                    enet_packet_destroy(event.packet);
                    break;
                case ENET_EVENT_TYPE_DISCONNECT:
                    handlePeerDisconnect(event.peer);
                    break;
                default:
                    break;
            }
        }

        roomsCleanupCheck();
        rateLimiter.cleanup(); // Clean up rate limiter data
    }
}

void STUNServer::cleanup() {
    if (server) {
        enet_host_destroy(server);
        server = nullptr;
    }
    enet_deinitialize();
}

void STUNServer::handlePeerMessage(ENetPeer* peer, const ENetPacket* packet) {
    // Check rate limit first
    if (!rateLimiter.isAllowed(peer->address.host)) {
        std::cerr << "Message from " << Int32ToIpv4(peer->address.host)
                  << " blocked due to rate limiting" << std::endl;
        return;
    }

    if (packet->dataLength == 0) {
        std::cerr << "Received empty packet from peer" << std::endl;
        return;
    }

    const uint8_t messageType = packet->data[0];

    switch (messageType) {
        case ReceiveHostRegister:
        case ReceiveClientRegister:
        {
            const bool isHost = messageType == ReceiveHostRegister;
            handleRegisterMessage(peer, packet, isHost);
            break;
        }
        case ReceiveHolePunched:
            handleHolePunchedMessage(peer);
            break;
        default:
            std::cerr << "Unknown message type: " << static_cast<int>(messageType) << std::endl;
            break;
    }
}

// Helper function to remove a peer from a specific room
void STUNServer::removePeerFromRoom(ENetPeer* peer, const std::string& roomName) {
    const auto roomIt = rooms.find(roomName);
    if (roomIt == rooms.end()) {
        std::cerr << "Warning: Attempted to remove peer from non-existent room '" << roomName << "'" << std::endl;
        return;
    }

    Room& room = roomIt->second;
    std::string peerNickname;
    bool found = false;

    // Check if it's the host
    if (room.host.enetPeer == peer) {
        peerNickname = room.host.nickname;
        std::cout << "Removing peer '" << peerNickname << "' from host position in room '" << roomName << "'" << std::endl;
        room.host = Peer(); // Reset host
        found = true;
    } else {
        // Search and remove from clients
        for (auto clientIt = room.clients.begin(); clientIt != room.clients.end(); ++clientIt) {
            if (clientIt->enetPeer == peer) {
                peerNickname = clientIt->nickname;
                std::cout << "Removing peer '" << peerNickname << "' from client list in room '" << roomName << "'" << std::endl;
                room.clients.erase(clientIt);
                found = true;
                break;
            }
        }
    }

    // Remove peer from the mapping
    if (found)
        peerToRoom.erase(peer);
    else
        std::cerr << "Warning: Peer not found in room '" << roomName << "' data structure" << std::endl;

    // Clean up empty room
    if (room.host.enetPeer == nullptr && room.clients.empty()) {
        std::cout << "Cleaning up empty room '" << roomName << "' after peer removal" << std::endl;
        rooms.erase(roomIt);
    }
}

void STUNServer::handleRegisterMessage(ENetPeer* peer, const ENetPacket* packet, const bool isHost) {
    if (packet->dataLength < 4) {
        std::cerr << "Registration packet too short" << std::endl;
        return;
    }

    size_t index = 1; // Skip message type byte

    // Parse structure: ipsCount, roomLength, nicknameLength, ips..., room, nickname
    const uint8_t ipsCount = packet->data[index++];
    const uint8_t roomLength = packet->data[index++];
    const uint8_t nicknameLength = packet->data[index++];

    // Validate packet length
    const size_t expectedLength = 4 + ipsCount * 4 + roomLength + nicknameLength;
    if (packet->dataLength != expectedLength) {
        std::cerr << "Invalid registration packet length" << std::endl;
        return;
    }

    std::vector<std::array<uint8_t, 4>> privateIps;
    privateIps.reserve(ipsCount);
    for (uint8_t i = 0; i < ipsCount; ++i) {
        if (index + 4 > packet->dataLength) {
            std::cerr << "Not enough data for IP address" << std::endl;
            return;
        }

        std::array ip = {
            packet->data[index++],
            packet->data[index++],
            packet->data[index++],
            packet->data[index++]
        };
        privateIps.push_back(ip);
    }

    // Parse room and nickname with ASCII validation
    for (size_t i = 0; i < roomLength; ++i) {
            unsigned char byte = packet->data[index + i];
            if (byte > 127) {
                std::cerr << "Invalid ASCII character in room name" << std::endl;
                return;
            }
        }
    auto roomName = std::string(reinterpret_cast<const char *>(packet->data + index), roomLength);
    index += roomLength;

    for (size_t i = 0; i < nicknameLength; ++i) {
            unsigned char byte = packet->data[index + i];
            if (byte > 127) {
                std::cerr << "Invalid ASCII character in nickname" << std::endl;
                return;
            }
        }
    const auto nickname = std::string(reinterpret_cast<const char *>(packet->data + index), nicknameLength);

    // Check if this peer is already connected and remove from previous room
    const auto existingPeerIt = peerToRoom.find(peer);
    if (existingPeerIt != peerToRoom.end()) {
        const std::string& oldRoomName = existingPeerIt->second;
        std::cout << "Peer reconnecting - removing from previous room '" << oldRoomName << "'" << std::endl;

        // Use helper function to remove from old room
        removePeerFromRoom(peer, oldRoomName);
    }

    // Find or create room
    auto roomIt = rooms.find(roomName);

    if (roomIt == rooms.end()) {
        // Room doesn't exist, create it
        auto [insertedIt, wasInserted] = rooms.emplace(roomName, Room());
        insertedIt->second.name = roomName; // Set the name
        roomIt = insertedIt;

        std::cout << "Created new room '" << roomName << "'" << std::endl;
    }

    Room& room = roomIt->second;

    // Create peer
    Peer newPeer;
    newPeer.enetPeer = peer;
    newPeer.nickname = nickname;
    newPeer.isMatched = false;
    newPeer.isHost = isHost;
    newPeer.privateIps = privateIps;

    std::cout << "Peer registered: " << nickname << " in room '" << roomName
              << "' as " << (isHost ? "host" : "client")
              << " (latency: " << newPeer.latency() << "ms)" << std::endl;

    MessageType responseType;

    if (isHost) {
        if (room.host.enetPeer != nullptr) {
            std::cerr << "Room '" << roomName << "' already has a host. Rejecting new host." << std::endl;
            responseType = SendRegisterFail;
        } else {
            room.host = newPeer;
            responseType = SendRegisterSuccess;
        }
    } else {
        responseType = SendRegisterSuccess;
        room.clients.push_back(newPeer);
    }

    // Update peer mapping (this will overwrite any previous mapping)
    peerToRoom[peer] = roomName;

    // Send response
    uint8_t responseData[3];
    responseData[0] = responseType;
    getBytesBigEndian(newPeer.port(), responseData + 1);

    ENetPacket* responsePacket = enet_packet_create(responseData, 3, ENET_PACKET_FLAG_RELIABLE);
    const int error = enet_peer_send(peer, 0, responsePacket);
    if (error != 0)
        std::cerr << "Failed to send register response to peer " << Int32ToIpv4(peer->address.host)  << ": " << error << std::endl;
    else
        std::cout << "Sent register response " << static_cast<uint8_t>(responseType) << " to peer " << Int32ToIpv4(peer->address.host) << std::endl;

    tryMatchPeers(roomName);
}

void STUNServer::handleHolePunchedMessage(ENetPeer* peer) {
    const auto peerIt = peerToRoom.find(peer);
    if (peerIt == peerToRoom.end()) {
        std::cerr << "Received hole punched message from unknown peer" << std::endl;
        return;
    }
    
    const std::string& roomName = peerIt->second;
    
    // Get the room
    auto roomIt = rooms.find(roomName);
    if (roomIt == rooms.end()) {
        std::cerr << "Peer mapped to non-existent room: " << roomName << std::endl;
        peerToRoom.erase(peerIt); // Clean up orphaned peer
        return;
    }
    
    Room& room = roomIt->second;
    
    Peer* currentPeer = nullptr;
    
    if (room.host.enetPeer == peer) {
        currentPeer = &room.host;
    } else {
        for (auto& client : room.clients) {
            if (client.enetPeer == peer) {
                currentPeer = &client;
                break;
            }
        }
    }
    
    if (currentPeer == nullptr) {
        std::cerr << "Peer mapping inconsistent - peer not found in room" << std::endl;
        peerToRoom.erase(peerIt); // Clean up inconsistent mapping
        return;
    }
    
    std::cout << "Client " << currentPeer->nickname << " completed hole punching" << std::endl;
    
    currentPeer->isMatched = false;
    
    // Check for available host
    if (room.host.enetPeer == nullptr || room.host.isMatched) {
        std::cout << "No available host found in room '" << roomName << "'" << std::endl;
        return;
    }
    
    int availableClients = 0;
    for (const auto& client : room.clients) {
        if (!client.isMatched) {
            availableClients++;
        }
    }
    
    std::cout << "Room '" << roomName << "' has host '" << room.host.nickname 
              << "' and " << availableClients << " available clients" << std::endl;
    
    if (availableClients >= 1) {
        std::cout << "Attempting to match next client with host in room '" << roomName << "'" << std::endl;
        tryMatchPeers(roomName);
    } else {
        std::cout << "No available clients to match with host in room '" << roomName << "'" << std::endl;
    }
}

void STUNServer::handlePeerDisconnect(ENetPeer* enetPeer) {
    auto peerIt = peerToRoom.find(enetPeer);
    if (peerIt == peerToRoom.end()) {
        std::cout << "Unknown peer disconnected" << std::endl;
        return;
    }
    
    const std::string& roomName = peerIt->second;

    // Use helper function to remove from room
    removePeerFromRoom(enetPeer, roomName);

    std::cout << "Peer disconnected from room " << roomName << std::endl;
}

std::string STUNServer::bytesToIpv4(const std::array<uint8_t, 4> bytes) {
    return std::format("{}.{}.{}.{}",
                       static_cast<int>(bytes[0]),
                       static_cast<int>(bytes[1]),
                       static_cast<int>(bytes[2]),
                       static_cast<int>(bytes[3]));
}

std::vector<uint8_t> STUNServer::ipv4ToBytes(const std::string& ip) {
    std::vector<uint8_t> bytes;
    std::stringstream ss(ip);
    std::string octet;

    while (std::getline(ss, octet, '.'))
        bytes.push_back(static_cast<uint8_t>(std::stoi(octet)));

    while (bytes.size() < 4)
        bytes.push_back(0);

    return bytes;
}

void STUNServer::tryMatchPeers(const std::string& roomName) {

    const auto roomIt = rooms.find(roomName);
    if (roomIt == rooms.end()) return; // Room doesn't exist
    
    Room& room = roomIt->second;

    auto host = room.host;

    if (room.clients.empty()) return;
    if (host.enetPeer == nullptr || host.isMatched) return;

    // Find the first available client
    Peer* client = nullptr;
    for (auto& peer : room.clients) {
        if (!peer.isMatched) {
            client = &peer;
            break;
        }
    }

    if (client == nullptr) return;

    // Match the host and client
    host.isMatched = true;
    client->isMatched = true;

    std::cout << "Matching clients: " << host.nickname
            << " (host, latency: " << host.latency() << "ms) with "
            << client->nickname << " (client, latency: " << client->latency() << "ms)" << std::endl;

    // Send peer info to both clients
    auto toHostPacket = createPeerInfoPacket(host, *client);
    auto toClientPacket = createPeerInfoPacket(*client, host);

    constexpr auto channel = 0;
    const int toHostError = enet_peer_send(host.enetPeer, channel, toHostPacket);
    const int toClientError = enet_peer_send(client->enetPeer, channel, toClientPacket);

    if (toHostError != 0)
        std::cerr << "Failed to send peer info to " << bytesToIpv4(host.publicIpv4()) << ": " << toHostError << std::endl;
    if (toClientError != 0)
        std::cerr << "Failed to send peer info to " << bytesToIpv4(host.publicIpv4()) << ": " << toHostError << std::endl;
}


ENetPacket* STUNServer::createPeerInfoPacket(const Peer &to, const Peer &about) {
    // Calculate packet size: 1 (message type) + 1 (ips length) + 4*N (ips) + 2 (port) + 2 (timestamp)
    const size_t ipsCount = about.privateIps.size();
    const size_t packetSize = 1 + 1 + 4 * ipsCount + 2 + 2;

    auto data = std::make_unique<uint8_t[]>(packetSize);
    size_t dataIndex = 0;

    // Message type
    data[dataIndex++] = SendPeerInfo;

    // IPs Length
    data[dataIndex++] = static_cast<uint8_t>(ipsCount);

    // IP addresses (4 bytes each)
    for (const auto& ip : about.privateIps) {
        for (size_t i = 0; i < 4; ++i) {
            data[dataIndex++] = ip[i];
        }
    }

    // Port (2 bytes, big endian)
    const uint16_t port = about.port();
    getBytesBigEndian(port, data.get() + dataIndex);
    dataIndex += 2;

    // Timestamp (2 bytes, big endian)
    constexpr uint16_t baseWaitMs = 200;
    const uint32_t toLatency = to.latency();
    const uint32_t aboutLatency = about.latency();
    const uint32_t maxLatency = std::max(toLatency, aboutLatency);
    const uint32_t calculatedWait = baseWaitMs + (3 * maxLatency - toLatency);
    constexpr uint32_t maxUint16 = 65535;
    const uint16_t waitTimeMs = static_cast<uint16_t>(std::min(calculatedWait, maxUint16));

    getBytesBigEndian(waitTimeMs, data.get() + dataIndex);
    dataIndex += 2;

    // Create packet with the calculated size
    return enet_packet_create(data.release(), packetSize, ENET_PACKET_FLAG_RELIABLE);
}

void STUNServer::roomsCleanupCheck() {
    const auto now = std::chrono::steady_clock::now();
    const auto timeSinceLastCleanup = std::chrono::duration_cast<std::chrono::minutes>(now - lastCleanupCheck);
    constexpr long checkupTimeMin = 30L;

    if (timeSinceLastCleanup < std::chrono::minutes(checkupTimeMin)) return;

    size_t cleanedRooms = 0;
    std::vector<std::string> roomsToRemove;
    
    for (auto& [roomName, room] : rooms) {
        if (room.hasExpired()) {
            std::cout << "Cleaning up expired room '" << roomName
                     << "' (created " << std::chrono::duration_cast<std::chrono::hours>(
                         std::chrono::steady_clock::now() - room.createdAt).count()
                     << " hours ago)" << std::endl;

            if (room.host.enetPeer != nullptr) {
                peerToRoom.erase(room.host.enetPeer);
                enet_peer_disconnect_now(room.host.enetPeer, 0);
            }

            for (auto& client : room.clients) {
                if (client.enetPeer != nullptr) {
                    peerToRoom.erase(client.enetPeer);
                    enet_peer_disconnect_now(client.enetPeer, 0);
                }
            }
            
            roomsToRemove.push_back(roomName);
        }
    }
    
    // Remove the expired rooms
    for (const auto& roomName : roomsToRemove) {
        rooms.erase(roomName);
        cleanedRooms++;
    }

    if (cleanedRooms > 0) {
        std::cout << "Cleaned up " << cleanedRooms << " expired room(s)" << std::endl;
    }

    lastCleanupCheck = now;
}

std::string STUNServer::Int32ToIpv4(const uint32_t ip) {

    return  std::to_string(ip & 0xFF) + "." +
            std::to_string(ip >> 8 & 0xFF) + "." +
            std::to_string(ip >> 16 & 0xFF) + "." +
            std::to_string(ip >> 24 & 0xFF);
}


bool STUNServer::isLittleEndian() {
    uint16_t test = 0x0001;
    return *reinterpret_cast<uint8_t*>(&test) == 0x01;
}

uint16_t STUNServer::toUInt16BigEndian(const uint8_t* data, const size_t startIndex, const size_t dataLength) {
    if (startIndex + 2 > dataLength)
        throw STUNServerException("Not enough bytes to read UInt16");

    if (!isLittleEndian()) {
        uint16_t result;
        std::memcpy(&result, data + startIndex, sizeof(uint16_t));
        return result;
    }

    return static_cast<uint16_t>(data[startIndex]) << 8 |
           static_cast<uint16_t>(data[startIndex + 1]);
}

uint32_t STUNServer::toUInt32BigEndian(const uint8_t* data, const size_t startIndex, const size_t dataLength) {
    if (startIndex + 4 > dataLength)
        throw STUNServerException("Not enough bytes to read UInt32");

    if (!isLittleEndian()) {
        uint32_t result;
        std::memcpy(&result, data + startIndex, sizeof(uint32_t));
        return result;
    }

    return static_cast<uint32_t>(data[startIndex]) << 24 |
           static_cast<uint32_t>(data[startIndex + 1]) << 16 |
           static_cast<uint32_t>(data[startIndex + 2]) << 8 |
           static_cast<uint32_t>(data[startIndex + 3]);
}

void STUNServer::getBytesBigEndian(const uint16_t value, uint8_t* output) {
    if (isLittleEndian()) { 
        output[0] = value >> 8 & 0xFF;
        output[1] = value & 0xFF;
    } else 
        std::memcpy(output, &value, sizeof(uint16_t));
}

void STUNServer::getBytesBigEndian(const uint32_t value, uint8_t* output) {
    if (isLittleEndian()) {
        output[0] = value >> 24 & 0xFF;
        output[1] = value >> 16 & 0xFF;
        output[2] = value >> 8 & 0xFF;
        output[3] = value & 0xFF;
    } else 
        std::memcpy(output, &value, sizeof(uint32_t));
}

// Rate limiter implementation
bool RateLimiter::isAllowed(uint32_t clientIP) {
    const auto now = std::chrono::steady_clock::now();
    auto& client = clientData[clientIP];
    
    // Check if client is currently blocked
    if (client.blockedUntil > now) {
        return false; // Still blocked
    }
    
    // Reset block status if it was previously blocked but time has passed
    if (client.blockedUntil != std::chrono::steady_clock::time_point{}) {
        client.blockedUntil = std::chrono::steady_clock::time_point{};
        client.requestCount = 0;
        client.windowStart = now;
    }
    
    // Check if we need to start a new time window
    const auto timeSinceWindowStart = now - client.windowStart;
    if (timeSinceWindowStart >= windowDuration) {
        // Start new window
        client.windowStart = now;
        client.requestCount = 1;
        return true;
    }
    
    // We're within the current window
    client.requestCount++;

    // Check if limit exceeded
    if (client.requestCount > maxRequestsPerWindow) {
        // Block this client
        client.blockedUntil = now + blockDuration;
        std::cout << "Rate limit exceeded for IP " << STUNServer::Int32ToIpv4(clientIP)
                  << " (blocked for " << blockDuration.count() << "ms)" << std::endl;
        return false;
    }
    
    return true;
}

void RateLimiter::cleanup() {
    const auto now = std::chrono::steady_clock::now();
    const auto timeSinceLastCleanup = now - lastCleanup;
    
    // Clean up every 5 minutes
    if (timeSinceLastCleanup < std::chrono::minutes(5L)) return;
    
    size_t removed = 0;
    
    for (auto it = clientData.begin(); it != clientData.end();) {
        const auto& client = it->second;
        const auto timeSinceLastSeen = now - std::max(client.windowStart, client.blockedUntil);
        
        // Remove if not seen for 10 minutes and not blocked
        if (timeSinceLastSeen > std::chrono::minutes(10L) && client.blockedUntil <= now) {
            it = clientData.erase(it);
            removed++;
        } else {
            ++it;
        }
    }
    
    if (removed > 0) {
        std::cout << "Rate limiter cleanup: removed " << removed << " old entries" << std::endl;
    }
    
    lastCleanup = now;
}
