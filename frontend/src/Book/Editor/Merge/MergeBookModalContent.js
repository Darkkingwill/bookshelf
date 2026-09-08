import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import styles from './MergeBookModalContent.css';

class MergeBookModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      targetBookId: null
    };
  }

  //
  // Listeners

  onTargetChange = (event) => {
    this.setState({ targetBookId: parseInt(event.target.value) });
  };

  onMergeConfirmed = () => {
    const { targetBookId } = this.state;

    if (!targetBookId) {
      return;
    }

    this.props.onMergeConfirmed(targetBookId);
  };

  //
  // Render

  render() {
    const {
      books,
      isMerging,
      mergeError,
      onModalClose
    } = this.props;

    const { targetBookId } = this.state;
    const sourceCount = books.length - 1;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          Merge Duplicate Books
        </ModalHeader>

        <ModalBody>
          <div className={styles.message}>
            Pick which of these {books.length} selected books is the one to keep. The other{sourceCount > 1 ? 's' : ''} will have {sourceCount > 1 ? 'their' : 'its'} files and history moved onto the one you keep, then be deleted. This cannot be undone.
          </div>

          <ul className={styles.list}>
            {
              books.map((book) => {
                const stats = book.statistics || {};

                return (
                  <li key={book.id} className={styles.item}>
                    <label className={styles.label}>
                      <input
                        type="radio"
                        name="targetBookId"
                        value={book.id}
                        checked={targetBookId === book.id}
                        onChange={this.onTargetChange}
                      />
                      <span className={styles.title}>{book.title}</span>
                      <span className={styles.stats}>
                        {stats.bookFileCount || 0} file{stats.bookFileCount === 1 ? '' : 's'}, {formatBytes(stats.sizeOnDisk || 0)}
                      </span>
                    </label>
                  </li>
                );
              })
            }
          </ul>

          {
            mergeError &&
              <Alert kind={kinds.DANGER}>
                Unable to merge these books
              </Alert>
          }
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            Cancel
          </Button>

          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isMerging}
            isDisabled={!targetBookId}
            onPress={this.onMergeConfirmed}
          >
            Merge
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

MergeBookModalContent.propTypes = {
  books: PropTypes.arrayOf(PropTypes.object).isRequired,
  isMerging: PropTypes.bool.isRequired,
  mergeError: PropTypes.object,
  onModalClose: PropTypes.func.isRequired,
  onMergeConfirmed: PropTypes.func.isRequired
};

export default MergeBookModalContent;
