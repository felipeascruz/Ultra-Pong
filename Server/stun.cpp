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

constexpr char RESERVED_CHAR = ':';

// STUNServerException implementation
STUNServerException::STUNServerException(const std::string& message)
    : std::runtime_error("STUN Server Error: " + message) {}

// Peer implementation
Peer::Peer() : enetPeer(nullptr), isMatched(false) {}

std::array<uint8_t, 4> Peer::publicIpv4() const {
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
    return roomAge >= ROOM_LIFETIME;
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

    server = enet_host_create(&address, 32, 2, 0, 0);
    if (server == nullptr) {
        enet_deinitialize();
        throw STUNServerException("Failed to create ENET host on port " + std::to_string(SERVER_PORT));
    }
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
    }
}

void STUNServer::roomsCleanupCheck() {
    const auto now = std::chrono::steady_clock::now();
    const auto timeSinceLastCleanup = std::chrono::duration_cast<std::chrono::minutes>(now - lastCleanupCheck);
    constexpr long checkupTimeMin = 30L;

    // Check for expired rooms every 30 minutes
    if (timeSinceLastCleanup < std::chrono::minutes(checkupTimeMin)) return;

    size_t cleanedRooms = 0;

    for (auto roomIterator = rooms.begin(); roomIterator != rooms.end();) {
        if (roomIterator->hasExpired()) {
            std::cout << "Cleaning up expired room '" << roomIterator->name
                     << "' (created " << std::chrono::duration_cast<std::chrono::hours>(
                         std::chrono::steady_clock::now() - roomIterator->createdAt).count()
                     << " hours ago)" << std::endl;

            // Disconnect all peers in the room
            if (roomIterator->host.enetPeer != nullptr) {
                enet_peer_disconnect_now(roomIterator->host.enetPeer, 0);
            }

            for (auto& client : roomIterator->clients) {
                if (client.enetPeer != nullptr) {
                    enet_peer_disconnect_now(client.enetPeer, 0);
                }
            }

            roomIterator = rooms.erase(roomIterator);
            cleanedRooms++;
        } else
            ++roomIterator;
    }

    if (cleanedRooms > 0) {
        std::cout << "Cleaned up " << cleanedRooms << " expired room(s)" << std::endl;
    }

    lastCleanupCheck = now;

}

void STUNServer::handlePeerConnect(ENetPeer*) {
    std::cout << "Peer connected" << std::endl;
}

void STUNServer::handlePeerMessage(ENetPeer* peer, const ENetPacket* packet) {
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

void STUNServer::handleRegisterMessage(ENetPeer* peer, const ENetPacket* packet, const bool isHost) {
    if (packet->dataLength < 2) {
        std::cerr << "Invalid register message length" << std::endl;
        return;
    }

    std::string roomNickname(reinterpret_cast<char*>(packet->data + 1), packet->dataLength - 1);

    const size_t separatorPos = roomNickname.find(RESERVED_CHAR);
    if (separatorPos == std::string::npos) return;

    const std::string roomName = roomNickname.substr(0, separatorPos);
    std::string nickname = roomNickname.substr(separatorPos + 1);

    if (nickname.length() > MAX_NICKNAME_LENGTH) {
        std::cerr << "Nickname too long (" << nickname.length() << " > " << MAX_NICKNAME_LENGTH 
                  << "), truncating..." << std::endl;
        nickname = nickname.substr(0, MAX_NICKNAME_LENGTH);
    }

    // Find or create room
    Room* room = nullptr;
    for (auto& r : rooms) {
        if (r.name == roomName) {
            room = &r;
            break;
        }
    }

    if (room == nullptr) {
        rooms.emplace_back();
        room = &rooms.back();
        room->name = roomName;
        // Note: createdAt is automatically set in Room constructor
        
        std::cout << "Created new room '" << roomName << "'" << std::endl;
    }

    // Create peer
    Peer newPeer;
    newPeer.enetPeer = peer;
    newPeer.nickname = nickname;
    newPeer.isMatched = false;

    std::cout << "Peer registered: " << nickname << " in room '" << roomName
              << "' as " << (isHost ? "host" : "client")
              << " (latency: " << newPeer.latency() << "ms)" << std::endl;

    if (isHost) {
        if (room->host.enetPeer != nullptr) {
            std::cerr << "Room '" << roomName << "' already has a host. Rejecting new host." << std::endl;
            return;
        }
        room->host = newPeer;
    } else {
        room->clients.push_back(newPeer);
        tryMatchPeers(roomName);
    }
}

void STUNServer::handleHolePunchedMessage(ENetPeer* peer) {

    // Find peer in rooms
    Peer* currentPeer = nullptr;
    Room* currentRoom = nullptr;
    
    for (auto& room : rooms) {
        if (room.host.enetPeer == peer) {
            currentPeer = &room.host;
            currentRoom = &room;
            break;
        }
        for (auto& client : room.clients) {
            if (client.enetPeer == peer) {
                currentPeer = &client;
                currentRoom = &room;
                break;
            }
        }
        if (currentPeer) break;
    }

    if (currentPeer == nullptr || currentRoom == nullptr) {
        std::cerr << "Received hole punched message from unknown peer" << std::endl;
        return;
    }
    
    std::cout << "Client " << currentPeer->nickname << " completed hole punching" << std::endl;
    
    currentPeer->isMatched = false;
    
    // Check for available host
    if (currentRoom->host.enetPeer == nullptr || currentRoom->host.isMatched) {
        std::cout << "No available host found in room '" << currentRoom->name << "'" << std::endl;
        return;
    }
    
    int availableClients = 0;
    for (const auto& client : currentRoom->clients) {
        if (!client.isMatched) {
            availableClients++;
        }
    }
    
    std::cout << "Room '" << currentRoom->name << "' has host '" << currentRoom->host.nickname 
              << "' and " << availableClients << " available clients" << std::endl;
    
    if (availableClients >= 1) {
        std::cout << "Attempting to match next client with host in room '" << currentRoom->name << "'" << std::endl;
        tryMatchPeers(currentRoom->name);
    } else {
        std::cout << "No available clients to match with host in room '" << currentRoom->name << "'" << std::endl;
    }
}

void STUNServer::tryMatchPeers(const std::string& roomName) {
    Room* room = nullptr;
    for (auto& r : rooms) {
        if (r.name == roomName) {
            room = &r;
            break;
        }
    }

    if (room == nullptr) return;
    if (room->clients.empty()) return;
    if (room->host.enetPeer == nullptr || room->host.isMatched) return;

    // Find the first available client
    Peer* availableClient = nullptr;
    for (auto& client : room->clients) {
        if (!client.isMatched) {
            availableClient = &client;
            break;
        }
    }

    if (availableClient == nullptr) return;

    // Match the host and client
    room->host.isMatched = true;
    availableClient->isMatched = true;

    std::cout << "Matching clients: " << room->host.nickname
              << " (host, latency: " << room->host.latency() << "ms) with "
              << availableClient->nickname << " (client, latency: " << availableClient->latency() << "ms)" << std::endl;

    // Send peer info to both clients
    sendPeerInfo(room->host, *availableClient);
    sendPeerInfo(*availableClient, room->host);
}

void STUNServer::sendPeerInfo(const Peer& to, const Peer& about) {

    byte data[8]; // 2 bytes for Timestamp + 4 bytes for Public IP + 2 bytes for Port

    size_t dataIndex = 0;

    // Timestamp (2 bytes, big endian)
    constexpr uint16_t baseWaitMs = 100;
    const uint32_t toLatency = to.latency();
    const uint32_t aboutLatency = about.latency();
    
    const uint32_t maxLatency = std::max(toLatency, aboutLatency);

    const uint32_t calculatedWait = baseWaitMs + (3 * maxLatency - toLatency);

    constexpr uint32_t maxUint16 = 65535;
    const uint16_t waitTimeMs = static_cast<uint16_t>(std::min(calculatedWait, maxUint16));

    getBytesBigEndian(waitTimeMs, data + dataIndex);
    dataIndex += 2;

    // Public IP (4 bytes)
    const auto publicIp = about.publicIpv4();
    for (size_t i = 0; i < 4; ++i)
        data[dataIndex++] = publicIp[i];

    // Port (2 bytes, big endian)
    const uint16_t port = about.port();
    getBytesBigEndian(port, data + dataIndex);
    dataIndex += 2;

    ENetPacket* packet = enet_packet_create(data, dataIndex, ENET_PACKET_FLAG_RELIABLE);
    enet_peer_send(to.enetPeer, 0, packet);

    std::cout << "Sent peer info about " << about.nickname
              << " to " << to.nickname << " (wait " << waitTimeMs << "ms, "
              << "sync calculation: " << toLatency << "ms latency, max is " << maxLatency << "ms)" << std::endl;
}

void STUNServer::handlePeerDisconnect(ENetPeer* enetPeer) {
    std::string disconnectedNickname;
    std::string roomName;
    bool found = false;

    // Find and remove peer from rooms
    for (auto roomIterator = rooms.begin(); roomIterator != rooms.end();) {
        Room& room = *roomIterator;
        
        // Check if it's the host
        if (room.host.enetPeer == enetPeer) {
            disconnectedNickname = room.host.nickname;
            roomName = room.name;
            room.host = Peer(); // Reset host
            found = true;
        }
        else {
            // Check if it's a client
            for (auto clientIt = room.clients.begin(); clientIt != room.clients.end(); ++clientIt) {
                if (clientIt->enetPeer == enetPeer) {
                    disconnectedNickname = clientIt->nickname;
                    roomName = room.name;
                    room.clients.erase(clientIt);
                    found = true;
                    break;
                }
            }
        }
        
        // Remove room if it's empty (no host and no clients)
        if (room.host.enetPeer == nullptr && room.clients.empty()) {
            std::cout << "Erasing '" << room.name << "' room" << std::endl;
            roomIterator = rooms.erase(roomIterator);
        } else {
            ++roomIterator;
        }
        
        if (found) break;
    }

    if (found) {
        std::cout << "Peer " << disconnectedNickname << " disconnected from room " << roomName << std::endl;
    } else {
        std::cout << "Unknown peer disconnected" << std::endl;
    }
}

std::string STUNServer::bytesToIpv4(const uint8_t* bytes) {
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

void STUNServer::cleanup() {
    if (server) {
        enet_host_destroy(server);
        server = nullptr;
    }
    enet_deinitialize();
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