#pragma once

#include <enet/enet.h>
#include <string>
#include <map>
#include <vector>
#include <stdexcept>
#include <cstdint>
#include <chrono>

constexpr int SERVER_PORT = 3478;
constexpr size_t MAX_NICKNAME_LENGTH = 256;
constexpr std::chrono::hours ROOM_LIFETIME(8L); // 8 hours room lifetime

using byte = uint8_t;

enum MessageTypes {
    SendPeerInfo,           // 1 byte for the message type, 4 bytes for public IP, 2 bytes for port
    ReceiveHostRegister,    // 1 byte for the message type, other bytes for room and client strings
    ReceiveClientRegister,  // 1 byte for the message type, other bytes for room and client strings
    ReceiveHolePunched      // 1 byte for the message type
};

class STUNServerException final : public std::runtime_error {
public:
    explicit STUNServerException(const std::string& message);
};

struct Peer {
    ENetPeer* enetPeer;
    std::string nickname;
    bool isMatched; // If it was already or is currently being matched with another peer for hole punching
    
    Peer();
    
    // Property-like getters
    std::array<uint8_t, 4> publicIpv4() const;
    uint16_t port() const { return enetPeer->address.port; }
    uint32_t latency() const { return enetPeer->roundTripTime / 2; }
};

struct Room {
    std::string name;
    Peer host;
    std::vector<Peer> clients;
    std::chrono::steady_clock::time_point createdAt;

    Room();
    
    // Check if room has expired
    bool hasExpired() const;
};


class STUNServer {
private:
    ENetHost* server;
    std::vector<Room> rooms;
    std::chrono::steady_clock::time_point lastCleanupCheck;

public:
    STUNServer();
    ~STUNServer();

    void initialize();
    void run();
    void cleanup();

private:
    static void handlePeerConnect(ENetPeer* peer);
    void handlePeerMessage(ENetPeer* peer, const ENetPacket* packet);
    void handleRegisterMessage(ENetPeer* peer, const ENetPacket* packet, bool isHost);
    void handleHolePunchedMessage(ENetPeer* peer);
    void handlePeerDisconnect(ENetPeer* enetPeer);

    static std::string bytesToIpv4(const uint8_t *bytes);
    static std::vector<uint8_t> ipv4ToBytes(const std::string& ip);

    void tryMatchPeers(const std::string& room);

    static void sendPeerInfo(const Peer& to, const Peer& about);
    
    // Room cleanup methods
    void roomsCleanupCheck();
    void cleanupExpiredRooms();

    // Endian conversion methods
    static bool isLittleEndian();

    static uint16_t toUInt16BigEndian(const uint8_t* data, size_t startIndex, size_t dataLength);

    static uint32_t toUInt32BigEndian(const uint8_t* data, size_t startIndex, size_t dataLength);

    static void getBytesBigEndian(uint16_t value, uint8_t* output);

    static void getBytesBigEndian(uint32_t value, uint8_t* output);
};