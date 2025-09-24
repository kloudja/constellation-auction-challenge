@bid @outbox @eventstore @us
Feature: PlaceBid appends to EventStore and enqueues Outbox in one commit

  Scenario: Place bid in US on Active auction
    Given Auction A is "Active" in US
    When  US places a bid Amount=310
    Then  US BID has a new row with Sequence = previous + 1
    And   US AUCTION.CurrentHighBid is 310 and RowVersion is incremented
    And   US EVENT_STORE has a "BidPlaced" with PayloadJson containing the BidId and CreatedAtUtc
    And   US EVENT_OUTBOX has a pending "BidPlaced" with Published=false
