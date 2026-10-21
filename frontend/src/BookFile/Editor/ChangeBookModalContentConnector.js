import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchBooks } from 'Store/Actions/bookActions';
import { updateBookFiles } from 'Store/Actions/bookFileActions';
import ChangeBookModalContent from './ChangeBookModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.books,
    (state) => state.bookFiles,
    (state, { authorId }) => authorId,
    (books, bookFiles, authorId) => {
      const items = books.items
        .filter((book) => book.authorId === authorId)
        .sort((a, b) => a.title.localeCompare(b.title));

      return {
        items,
        isFetching: books.isFetching,
        isPopulated: books.isPopulated,
        isSaving: bookFiles.isSaving,
        saveError: bookFiles.saveError
      };
    }
  );
}

const mapDispatchToProps = {
  fetchBooks,
  updateBookFiles
};

class ChangeBookModalContentConnector extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      pending: null,
      noEditionBookId: null,
      hasSubmitted: false
    };
  }

  componentDidMount() {
    // The author detail page already holds this author's books, but the book detail page
    // and a direct link do not, so only fetch when there is nothing to show.
    if (!this.props.items.length) {
      this.props.fetchBooks({ authorId: this.props.authorId });
    }
  }

  componentDidUpdate(prevProps) {
    const {
      isSaving,
      saveError
    } = this.props;

    // Only close once the server has accepted the change, so a failure stays visible.
    if (this.state.hasSubmitted && prevProps.isSaving && !isSaving && !saveError) {
      this.props.onModalClose(true);
    }
  }

  //
  // Listeners

  onBookSelect = (bookId, editionId) => {
    if (!editionId) {
      this.setState({ noEditionBookId: bookId });
      return;
    }

    this.setState({
      pending: { bookId, editionId },
      noEditionBookId: null,
      hasSubmitted: false
    });
  };

  onConfirm = () => {
    const { pending } = this.state;

    this.setState({ hasSubmitted: true });

    this.props.updateBookFiles({
      bookFileIds: this.props.bookFileIds,
      editionId: pending.editionId
    });
  };

  onBack = () => {
    this.setState({ pending: null, hasSubmitted: false });
  };

  //
  // Render

  render() {
    const {
      items,
      isFetching,
      isSaving,
      saveError,
      bookFileIds,
      currentBookId,
      onModalClose
    } = this.props;

    const {
      pending,
      noEditionBookId,
      hasSubmitted
    } = this.state;

    return (
      <ChangeBookModalContent
        items={items}
        isFetching={isFetching}
        isSaving={isSaving}
        hasSaveError={hasSubmitted && !isSaving && !!saveError}
        fileCount={bookFileIds.length}
        currentBookId={currentBookId}
        pendingBook={pending ? items.find((book) => book.id === pending.bookId) : null}
        noEditionBook={noEditionBookId ? items.find((book) => book.id === noEditionBookId) : null}
        onBookSelect={this.onBookSelect}
        onConfirm={this.onConfirm}
        onBack={this.onBack}
        onModalClose={onModalClose}
      />
    );
  }
}

ChangeBookModalContentConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  bookFileIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  currentBookId: PropTypes.number,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isFetching: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  fetchBooks: PropTypes.func.isRequired,
  updateBookFiles: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(ChangeBookModalContentConnector);
