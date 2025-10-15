#pragma once

#include <enet/enet.h>
#include <string>
#include <map>
#include <vector>
#include <stdexcept>
#include <cstdint>
#include <chrono>
#include <unordered_map>

constexpr int SERVER_PORT = 3478;
constexpr size_t MAX_NICKNAME_LENGTH = 256;
constexpr std::chrono::hours ROOM_LIFETIME_HOURS(8L);

enum MessageType : uint8_t {
    ReceiveHostRegister,    // 1 byte for the message type, other bytes for room and client strings
    ReceiveClientRegister,  // 1 byte for the message type, other bytes for room and client strings
    ReceiveHolePunched,     // 1 byte for the message type
    SendRegisterFail,       // 1 byte for the message type
    SendRegisterSuccess,    // 1 byte for the message type, 2 bytes for Port
    SendPeerInfo            // 1 byte for the message type, 4 bytes for public IP, 2 bytes for port
};

class STUNServerException final : public std::runtime_error {
public:
    explicit STUNServerException(const std::string& message);
};

struct Peer {
    ENetPeer* enetPeer;
    std::string nickname;
    bool isHost;
    bool isMatched; // If it was already or is currently being matched with another peer for hole punching
    std::vector<std::array<uint8_t, 4>> privateIps;

    Peer();
    
    // Property-like getters
    [[nodiscard]] std::array<uint8_t, 4> publicIpv4() const;
    [[nodiscard]] uint16_t port() const { return enetPeer->address.port; }
    [[nodiscard]] uint32_t latency() const { return enetPeer->roundTripTime / 2; }
};

struct Room {
    std::string name;
    Peer host;
    std::vector<Peer> clients;
    std::chrono::steady_clock::time_point createdAt;

    Room();
    
    // Check if the room has expired
    [[nodiscard]] bool hasExpired() const;
};

class RateLimiter {
private:
    struct ClientData {
        std::chrono::steady_clock::time_point windowStart;
        size_t requestCount;
        std::chrono::steady_clock::time_point blockedUntil;

        ClientData() : windowStart(std::chrono::steady_clock::now()), requestCount(0) {}
    };

    std::unordered_map<uint32_t, ClientData> clientData; // IP -> data
    std::chrono::milliseconds windowDuration;
    size_t maxRequestsPerWindow;
    std::chrono::milliseconds blockDuration;
    std::chrono::steady_clock::time_point lastCleanup;

public:
    explicit RateLimiter(
        std::chrono::milliseconds window = std::chrono::milliseconds(1000L),  // 1 second window
        size_t maxRequests = 10,                                            // Max 10 requests per second
        std::chrono::milliseconds blockTime = std::chrono::milliseconds(30000L) // Block for 30 seconds
    ) : windowDuration(window), maxRequestsPerWindow(maxRequests),
        blockDuration(blockTime), lastCleanup(std::chrono::steady_clock::now()) {}

    bool isAllowed(uint32_t clientIP);
    void cleanup();
};


class STUNServer {
private:
    ENetHost* server;
    std::unordered_map<std::string, Room> rooms;
    std::unordered_map<ENetPeer*, std::string> peerToRoom; // Used for performance: peer lookups become O(1)
    std::chrono::steady_clock::time_point lastCleanupCheck;
    RateLimiter rateLimiter;

public:
    STUNServer();
    ~STUNServer();

    void initialize();
    void run();
    void cleanup();
    static std::string Int32ToIpv4(uint32_t ip);

    // Add this declaration to the STUNServer class in stun.h
private:
    // Helper function to remove a peer from a specific room
    void removePeerFromRoom(ENetPeer* peer, const std::string& roomName);
    static void handlePeerConnect(ENetPeer* peer);
    void handlePeerMessage(ENetPeer* peer, const ENetPacket* packet);
    void handleRegisterMessage(ENetPeer* peer, const ENetPacket* packet, bool isHost);
    void handleHolePunchedMessage(ENetPeer* peer);
    void handlePeerDisconnect(ENetPeer* enetPeer);

    static std::string bytesToIpv4(std::array<uint8_t, 4> bytes);
    static std::vector<uint8_t> ipv4ToBytes(const std::string& ip);

    void tryMatchPeers(const std::string& roomName);

    static ENetPacket *createPeerInfoPacket(const Peer &to, const Peer &about);

    void roomsCleanupCheck();

    // Endian conversion methods
    static bool isLittleEndian();

    static uint16_t toUInt16BigEndian(const uint8_t* data, size_t startIndex, size_t dataLength);

    static uint32_t toUInt32BigEndian(const uint8_t* data, size_t startIndex, size_t dataLength);

    static void getBytesBigEndian(uint16_t value, uint8_t* output);

    static void getBytesBigEndian(uint32_t value, uint8_t* output);
};